#Requires -Version 7.0
<#
.SYNOPSIS
    Walks a tree produced by Export-DatabaseObjects.ps1 and builds a
    catalog.json consumed by the React catalog/lineage site (see site/).

.DESCRIPTION
    Every extracted object becomes a "node" (server/database/schema/type/
    name, DDL text, and any sys.extended_properties documentation found for
    it). "Lineage" edges are then inferred by scanning each object's DDL
    text for other known object names:

      1. Qualified references ("schema.object", brackets stripped) are
         matched against every other object in the same server+database.
      2. Bare references (a single identifier, 4+ characters) are matched
         only when exactly one object in the same server+database (or, for
         linked servers / database links, the same server) has that name -
         an ambiguous bare name is dropped rather than guessed at.

    This is regex-based text matching, not a real T-SQL/PL-SQL parser, so
    treat the resulting graph as a best-effort starting point for
    exploration, not a certified lineage report. It will miss dynamic SQL,
    four-part cross-linked-server names, and anything built at runtime; it
    can also occasionally produce a false-positive edge when an identifier
    happens to collide with an unrelated object name.

    When -RepoRoot is supplied, this also mines git history for the
    -PathPrefix folder inside that checkout (bounded to -HistoryLimit
    commits): a global recent-changes timeline, a per-object change count /
    last-changed date / bounded version history (with DDL content pulled
    via `git show`, for the point-in-time viewer), and co-change pairs
    (objects that tend to change in the same commit). Commits touching more
    than -MaxCoChangeCommitSize files are excluded from co-change pairing -
    almost always a bulk/initial sync rather than a meaningful "these
    objects change together" signal. Without -RepoRoot, all of this is
    simply omitted (empty arrays / zero counts) rather than failing.

.PARAMETER ObjectsRoot
    Root of the extracted tree (Export-DatabaseObjects.ps1's -StagingRoot).

.PARAMETER OutputPath
    File path the catalog JSON is written to.

.PARAMETER RepoRoot
    Path to a git checkout containing -PathPrefix with the extracted
    objects' commit history (typically this project's own working copy in
    CI). Omit to skip history/heatmap/point-in-time mining entirely.

.PARAMETER PathPrefix
    Folder inside -RepoRoot holding the extracted tree (config.git.pathPrefix,
    "objects" by default).

.PARAMETER HistoryLimit
    Maximum number of commits (touching -PathPrefix) to mine.

.PARAMETER MaxVersionsPerObject
    Maximum number of historical versions kept (and content-fetched) per
    object, most recent first.

.PARAMETER MaxHistoryContentCalls
    Hard cap on total `git show` invocations across the whole mining pass,
    so a large/old repo can't turn this into an unbounded CI job.

.PARAMETER MaxCoChangeCommitSize
    Commits touching more files than this are excluded from co-change
    pair counting.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ObjectsRoot,
    [Parameter(Mandatory)][string]$OutputPath,
    [string]$RepoRoot,
    [string]$PathPrefix = 'objects',
    [int]$HistoryLimit = 250,
    [int]$MaxVersionsPerObject = 15,
    [int]$MaxHistoryContentCalls = 1500,
    [int]$MaxCoChangeCommitSize = 40
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# No -Force: this script can run nested inside another module's still-executing
# function (Export-DatabaseObjects.ps1 -> Publish-SyncSqlToGit's -PostSyncHook),
# and forcing a reimport there has been observed to drop that caller's own
# resolved reference to this module's exported functions out from under it.
if (-not (Get-Module -Name SyncSql.Common)) {
    Import-Module (Join-Path $PSScriptRoot 'modules/SyncSql.Common.psm1')
}

if (-not (Test-Path -LiteralPath $ObjectsRoot)) {
    throw "Objects root not found: $ObjectsRoot"
}

function ConvertFrom-SyncSqlObjectFile {
    param([Parameter(Mandatory)][string]$FilePath)
    return ConvertFrom-SyncSqlObjectFileLines -Lines @(Get-Content -LiteralPath $FilePath)
}

function ConvertFrom-SyncSqlObjectFileLines {
    <#
        Core parser, reusable both for a file on disk (ConvertFrom-SyncSqlObjectFile)
        and for `git show <sha>:<path>` output (historical versions) which
        never touches disk.
    #>
    param([Parameter(Mandatory)][AllowEmptyCollection()][AllowEmptyString()][string[]]$Lines)

    $allLines = @($Lines)
    $i = 0
    while ($i -lt $allLines.Count -and $allLines[$i].StartsWith('-- ')) { $i++ }
    if ($i -lt $allLines.Count -and [string]::IsNullOrEmpty($allLines[$i])) { $i++ }

    # NOTE: @() must wrap the *whole* if/else statement, not each branch -
    # PowerShell unrolls a script block's output stream when collecting it
    # into a variable, so a single-element array returned from a branch
    # collapses back into a scalar unless the enclosing statement is itself
    # wrapped in @().
    $bodyLines = @(if ($i -lt $allLines.Count) { $allLines[$i..($allLines.Count - 1)] } else { @() })

    # Find every "-- === Title ===" marker (Foreign Keys, Check Constraints,
    # Indexes, Statistics, Extended Properties, ...). The DDL is everything
    # before the first marker; each marker starts a section running to the
    # next marker (or end of file).
    $markerPositions = [System.Collections.Generic.List[object]]::new()
    for ($j = 0; $j -lt $bodyLines.Count; $j++) {
        if ($bodyLines[$j] -match '^-- === (.+) ===$') {
            $markerPositions.Add([pscustomobject]@{ Index = $j; Title = $Matches[1] })
        }
    }

    if ($markerPositions.Count -gt 0) {
        $firstMarker = $markerPositions[0].Index
        $ddlLines = @(if ($firstMarker -gt 0) { $bodyLines[0..($firstMarker - 1)] } else { @() })
    }
    else {
        $ddlLines = $bodyLines
    }

    $sections = [System.Collections.Generic.List[object]]::new()
    for ($k = 0; $k -lt $markerPositions.Count; $k++) {
        $start = $markerPositions[$k].Index + 1
        $end = if ($k + 1 -lt $markerPositions.Count) { $markerPositions[$k + 1].Index - 1 } else { $bodyLines.Count - 1 }
        $sectionLines = @(if ($start -le $end) { $bodyLines[$start..$end] } else { @() })
        $sections.Add([pscustomobject]@{ Title = $markerPositions[$k].Title; Lines = $sectionLines })
    }

    $objectDescription = $null
    $columns = [ordered]@{}
    $extendedProperties = $sections | Where-Object { $_.Title -eq 'Extended Properties' } | Select-Object -First 1
    if ($extendedProperties) {
        foreach ($line in $extendedProperties.Lines) {
            # Capture $Matches into a local right after each successful -match:
            # the property-name check below is itself a -match, which would
            # otherwise silently clobber $Matches before it's read for real.
            if ($line -match '^-- \[object\] (\S+) = (.*)$') {
                $m = $Matches
                if ($m[1] -match 'Description' -and -not $objectDescription) { $objectDescription = $m[2] }
            }
            elseif ($line -match '^-- \[column: (.+?)\] (\S+) = (.*)$') {
                $m = $Matches
                if ($m[2] -match 'Description') { $columns[$m[1]] = $m[3] }
            }
        }
    }

    # Full ordinal column list (name + data type), when the extraction
    # backend attached one (Tables/Views only) - independent of whether any
    # of those columns happen to have an Extended Properties description.
    # Falls back to Extended-Properties-only columns (order not guaranteed)
    # when absent, same as before this section existed.
    $fullColumns = [System.Collections.Generic.List[object]]::new()
    $columnsSection = $sections | Where-Object { $_.Title -eq 'Columns' } | Select-Object -First 1
    if ($columnsSection) {
        foreach ($line in $columnsSection.Lines) {
            if ($line -match '^-- \[col\] ([^|]*)\|(.*)$') {
                $m = $Matches
                $fullColumns.Add([pscustomobject]@{ Name = $m[1]; DataType = $m[2] })
            }
        }
    }

    # Object/column-level grants ("who can touch this object"), when the
    # extraction backend attached one - see ConvertTo-SyncSqlGrantLine in
    # SyncSql.MsSql.psm1 for the line format both backends emit.
    $grants = [System.Collections.Generic.List[object]]::new()
    $grantsSection = $sections | Where-Object { $_.Title -eq 'Grants' } | Select-Object -First 1
    if ($grantsSection) {
        foreach ($line in $grantsSection.Lines) {
            if ($line -match '^-- \[grant\] ([^|]*)\|([^|]*)\|([^|]*)\|([^|]*)\|(.*)$') {
                $m = $Matches
                $grants.Add([ordered]@{
                        permission  = $m[1]
                        state       = $m[2]
                        grantee     = $m[3]
                        granteeType = if ([string]::IsNullOrEmpty($m[4])) { $null } else { $m[4] }
                        column      = if ([string]::IsNullOrEmpty($m[5])) { $null } else { $m[5] }
                    })
            }
        }
    }

    $otherSections = @($sections | Where-Object { $_.Title -notin @('Extended Properties', 'Columns', 'Grants') } | ForEach-Object {
        [ordered]@{ title = $_.Title; content = ($_.Lines -join "`n").Trim() }
    })

    return [pscustomobject]@{
        Ddl          = ($ddlLines -join "`n").Trim()
        Description  = $objectDescription
        Columns      = $columns
        FullColumns  = $fullColumns
        Grants       = $grants
        Sections     = $otherSections
    }
}

Write-SyncSqlLog "Scanning $ObjectsRoot"
$files = @(Get-ChildItem -LiteralPath $ObjectsRoot -Recurse -File -Filter '*.sql')
Write-SyncSqlLog "Found $($files.Count) object file(s)"

$nodes = [System.Collections.Generic.List[object]]::new()
$qualifiedIndex = @{}   # "server::database::schema.name" -> nodeId
$bareIndexDb = @{}      # "server::database::name"        -> [nodeId, ...]
$bareIndexServer = @{}  # "server::name"                  -> [nodeId, ...]

foreach ($file in $files) {
    $relative = [IO.Path]::GetRelativePath($ObjectsRoot, $file.FullName) -replace '\\', '/'
    $segments = $relative -split '/'
    if ($segments.Count -lt 4) {
        Write-SyncSqlLog "Skipping unexpected path shape: $relative" -Level WARN
        continue
    }

    $server = $segments[0]
    $database = $segments[1]
    $type = $segments[2]
    $name = [IO.Path]::GetFileNameWithoutExtension($segments[-1])
    $schema = if ($segments.Count -eq 5) { $segments[3] } else { $null }

    $parsed = ConvertFrom-SyncSqlObjectFile -FilePath $file.FullName
    $id = $relative -replace '\.sql$', ''
    $qualifiedName = if ($schema) { "$schema.$name" } else { $name }

    # Prefer the full structural column list (Tables/Views) when the
    # extraction backend attached one, merging in any Extended Properties
    # description found for that column; otherwise fall back to whatever
    # columns happened to have a description (the only ones we used to know
    # about at all).
    $columns = if ($parsed.FullColumns.Count -gt 0) {
        @($parsed.FullColumns | ForEach-Object {
                $description = if ($parsed.Columns.Contains($_.Name)) { $parsed.Columns[$_.Name] } else { $null }
                [ordered]@{ name = $_.Name; description = $description; dataType = $_.DataType }
            })
    }
    else {
        @($parsed.Columns.Keys | ForEach-Object { [ordered]@{ name = $_; description = $parsed.Columns[$_]; dataType = $null } })
    }

    $node = [ordered]@{
        id            = $id
        server        = $server
        database      = $database
        schema        = $schema
        type          = $type
        name          = $name
        qualifiedName = $qualifiedName
        path          = $relative
        ddl           = $parsed.Ddl
        description   = $parsed.Description
        columns       = $columns
        grants        = @($parsed.Grants)
        sections      = $parsed.Sections
        sizeBytes     = [Text.Encoding]::UTF8.GetByteCount($parsed.Ddl)
    }
    $nodes.Add($node)

    if ($schema) {
        $qualifiedIndex["${server}::${database}::$($schema.ToLowerInvariant()).$($name.ToLowerInvariant())"] = $id
    }

    $dbKey = "${server}::${database}::$($name.ToLowerInvariant())"
    if (-not $bareIndexDb.ContainsKey($dbKey)) { $bareIndexDb[$dbKey] = [System.Collections.Generic.List[string]]::new() }
    $bareIndexDb[$dbKey].Add($id)

    $serverKey = "${server}::$($name.ToLowerInvariant())"
    if (-not $bareIndexServer.ContainsKey($serverKey)) { $bareIndexServer[$serverKey] = [System.Collections.Generic.List[string]]::new() }
    $bareIndexServer[$serverKey].Add($id)
}

Write-SyncSqlLog "Inferring lineage edges (best-effort text matching)"
$edgeSet = [System.Collections.Generic.HashSet[string]]::new()
$edges = [System.Collections.Generic.List[object]]::new()

function Add-SyncSqlEdge {
    param([string]$From, [string]$To)
    if ($From -eq $To) { return }
    $key = "$From|$To"
    if ($edgeSet.Add($key)) {
        $edges.Add([ordered]@{ from = $From; to = $To }) | Out-Null
    }
}

$qualifiedPattern = [regex]'\[?([A-Za-z_][\w$#]*)\]?\.\[?([A-Za-z_][\w$#]*)\]?'
$barePattern = [regex]'\b([A-Za-z_][\w$#]{3,})\b'

foreach ($node in $nodes) {
    # Scan the DDL plus the (high-confidence, structural) Foreign Keys
    # section - other appended sections (Check Constraints, Indexes,
    # Statistics) don't reference other objects and would just add noise.
    $fkSection = $node.sections | Where-Object { $_.title -eq 'Foreign Keys' } | Select-Object -First 1
    $scanText = if ($fkSection) { $node.ddl + "`n" + $fkSection.content } else { $node.ddl }
    if ([string]::IsNullOrWhiteSpace($scanText)) { continue }

    foreach ($match in $qualifiedPattern.Matches($scanText)) {
        $schemaLower = $match.Groups[1].Value.ToLowerInvariant()
        $nameLower = $match.Groups[2].Value.ToLowerInvariant()
        $key = "$($node.server)::$($node.database)::$schemaLower.$nameLower"
        if ($qualifiedIndex.ContainsKey($key)) {
            Add-SyncSqlEdge -From $node.id -To $qualifiedIndex[$key]
        }
    }

    $seenBareTokens = [System.Collections.Generic.HashSet[string]]::new()
    foreach ($match in $barePattern.Matches($scanText)) {
        $tokenLower = $match.Value.ToLowerInvariant()
        if (-not $seenBareTokens.Add($tokenLower)) { continue }

        $dbKey = "$($node.server)::$($node.database)::$tokenLower"
        if ($bareIndexDb.ContainsKey($dbKey) -and $bareIndexDb[$dbKey].Count -eq 1) {
            Add-SyncSqlEdge -From $node.id -To $bareIndexDb[$dbKey][0]
            continue
        }

        $serverKey = "$($node.server)::$tokenLower"
        if ($bareIndexServer.ContainsKey($serverKey) -and $bareIndexServer[$serverKey].Count -eq 1) {
            Add-SyncSqlEdge -From $node.id -To $bareIndexServer[$serverKey][0]
        }
    }
}

Write-SyncSqlLog "Detecting column-level references for inferred edges (best-effort)"

# Words that can immediately follow a bare table/view reference without an
# explicit alias (FROM dbo.Orders WHERE ...) and would otherwise be
# misread as an alias by Get-SyncSqlAliasesForTarget's "next token" regex.
$script:SyncSqlAliasStopWords = @(
    'WHERE', 'ON', 'JOIN', 'INNER', 'LEFT', 'RIGHT', 'OUTER', 'FULL', 'CROSS', 'GROUP', 'ORDER',
    'HAVING', 'UNION', 'SET', 'VALUES', 'INTO', 'SELECT', 'FROM', 'AND', 'OR', 'NOT', 'NULL',
    'IS', 'IN', 'EXISTS', 'BEGIN', 'END', 'GO', 'WITH', 'FOR', 'BY', 'ALL', 'DISTINCT', 'TOP', 'AS'
)

function Get-SyncSqlAliasesForTarget {
    <#
        Best-effort alias resolution: finds tokens immediately following the
        target object's bare or schema-qualified name in $ScanText (covers
        "FROM tbl t", "JOIN sch.tbl AS t", and bare "tbl.column" usage with
        no alias at all, since the object's own name is always included).
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][AllowEmptyString()][string]$ScanText,
        [Parameter(Mandatory)]$TargetNode
    )

    $aliases = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    $aliases.Add($TargetNode.name) | Out-Null
    if ([string]::IsNullOrWhiteSpace($ScanText)) { return $aliases }

    $namePattern = [regex]::Escape($TargetNode.name)
    $qualifiedPrefix = if ($TargetNode.schema) { "(?:\[?$([regex]::Escape($TargetNode.schema))\]?\.)?" } else { '' }
    $aliasRegex = [regex]::new("(?i)$qualifiedPrefix\[?$namePattern\]?\s+(?:AS\s+)?\[?([A-Za-z_][\w$#]*)\]?")
    foreach ($m in $aliasRegex.Matches($ScanText)) {
        $alias = $m.Groups[1].Value
        if ($script:SyncSqlAliasStopWords -notcontains $alias.ToUpperInvariant()) {
            $aliases.Add($alias) | Out-Null
        }
    }
    return $aliases
}

function Get-SyncSqlUsedColumns {
    <#
        Scans $ScanText for "<alias>.<column>" references (alias being any
        of the target's resolved aliases) and returns the distinct matched
        column names - the best-effort "column dependency" signal attached
        to each inferred edge.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][AllowEmptyString()][string]$ScanText,
        [Parameter(Mandatory)]$Aliases,
        [Parameter(Mandatory)][string[]]$ColumnNames
    )

    if ($Aliases.Count -eq 0 -or $ColumnNames.Count -eq 0 -or [string]::IsNullOrWhiteSpace($ScanText)) { return @() }

    $aliasAlt = (($Aliases | ForEach-Object { [regex]::Escape($_) }) -join '|')
    $colAlt = (($ColumnNames | ForEach-Object { [regex]::Escape($_) }) -join '|')
    $regex = [regex]::new("(?i)\b(?:$aliasAlt)\.\[?($colAlt)\]?\b")

    $used = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($m in $regex.Matches($ScanText)) { $used.Add($m.Groups[1].Value) | Out-Null }
    return @($used)
}

$nodesByIdForColumns = @{}
foreach ($n in $nodes) { $nodesByIdForColumns[$n.id] = $n }

foreach ($edge in $edges) {
    $edge['columns'] = @()
    $toNode = $nodesByIdForColumns[$edge.to]
    $fromNode = $nodesByIdForColumns[$edge.from]
    if (-not $toNode -or -not $fromNode) { continue }
    if (-not $toNode.columns -or $toNode.columns.Count -eq 0) { continue }

    $columnNames = @($toNode.columns | ForEach-Object { $_.name } | Where-Object { $_ })
    if ($columnNames.Count -eq 0) { continue }

    $aliases = Get-SyncSqlAliasesForTarget -ScanText $fromNode.ddl -TargetNode $toNode
    $used = Get-SyncSqlUsedColumns -ScanText $fromNode.ddl -Aliases $aliases -ColumnNames $columnNames
    if ($used.Count -gt 0) {
        $edge['columns'] = @($used | Sort-Object)
    }
}

Write-SyncSqlLog "Built $($nodes.Count) node(s), $($edges.Count) edge(s)"

$typeCounts = [ordered]@{}
foreach ($node in $nodes) {
    if (-not $typeCounts.Contains($node.type)) { $typeCounts[$node.type] = 0 }
    $typeCounts[$node.type]++
    # Defaults so the JSON shape is stable whether or not history mining
    # below actually runs.
    $node['changeCount'] = 0
    $node['lastChangedAt'] = $null
    $node['history'] = @()
}

$recentChanges = @()
$coChangePairs = @()

if ($RepoRoot) {
    $gitDir = Join-Path $RepoRoot '.git'
    if (-not (Test-Path -LiteralPath $gitDir)) {
        Write-SyncSqlLog "RepoRoot '$RepoRoot' is not a git checkout (no .git) - skipping history mining." -Level WARN
    }
    else {
        Write-SyncSqlLog "Mining up to $HistoryLimit commit(s) of git history under '$PathPrefix' in $RepoRoot"
        try {
            $rawLog = & git -C $RepoRoot log -n $HistoryLimit --date=iso-strict --pretty=format:'@@COMMIT@@%H@@%ad@@%s' --name-only -- $PathPrefix 2>&1
            if ($LASTEXITCODE -ne 0) { throw "git log exited $LASTEXITCODE`: $($rawLog -join ' ')" }

            $commits = [System.Collections.Generic.List[object]]::new()
            $current = $null
            foreach ($line in @($rawLog)) {
                if ($line.StartsWith('@@COMMIT@@')) {
                    if ($current) { $commits.Add($current) }
                    $parts = $line.Substring(10) -split '@@', 3
                    $current = [ordered]@{ sha = $parts[0]; date = $parts[1]; message = $parts[2]; files = [System.Collections.Generic.List[string]]::new() }
                }
                elseif ($current -and -not [string]::IsNullOrWhiteSpace($line)) {
                    $current.files.Add($line.Trim())
                }
            }
            if ($current) { $commits.Add($current) }

            Write-SyncSqlLog "Found $($commits.Count) commit(s) touching '$PathPrefix'"

            $nodesById = @{}
            foreach ($node in $nodes) { $nodesById[$node.id] = $node }

            $objectHistory = @{}
            $coChangeCounts = @{}
            $prefixLen = $PathPrefix.Length + 1

            foreach ($commit in $commits) {
                $objectIds = @($commit.files | Where-Object { $_.StartsWith("$PathPrefix/") -and $_.EndsWith('.sql') } |
                    ForEach-Object { $_.Substring($prefixLen) -replace '\.sql$', '' } |
                    Where-Object { $nodesById.ContainsKey($_) })
                if ($objectIds.Count -eq 0) { continue }

                $recentChanges += [ordered]@{ sha = $commit.sha; date = $commit.date; message = $commit.message; objectIds = $objectIds }

                foreach ($id in $objectIds) {
                    $node = $nodesById[$id]
                    $node['changeCount'] = [int]$node['changeCount'] + 1
                    if (-not $node['lastChangedAt']) { $node['lastChangedAt'] = $commit.date }

                    if (-not $objectHistory.ContainsKey($id)) { $objectHistory[$id] = [System.Collections.Generic.List[object]]::new() }
                    if ($objectHistory[$id].Count -lt $MaxVersionsPerObject) {
                        $objectHistory[$id].Add([ordered]@{ sha = $commit.sha; date = $commit.date; message = $commit.message; ddl = $null })
                    }
                }

                if ($objectIds.Count -gt 1 -and $objectIds.Count -le $MaxCoChangeCommitSize) {
                    $sortedIds = @($objectIds | Sort-Object -Unique)
                    for ($x = 0; $x -lt $sortedIds.Count; $x++) {
                        for ($y = $x + 1; $y -lt $sortedIds.Count; $y++) {
                            $pairKey = "$($sortedIds[$x])|$($sortedIds[$y])"
                            if (-not $coChangeCounts.ContainsKey($pairKey)) { $coChangeCounts[$pairKey] = 0 }
                            $coChangeCounts[$pairKey]++
                        }
                    }
                }
            }

            Write-SyncSqlLog "Fetching historical DDL content (up to $MaxHistoryContentCalls `git show` call(s))"
            $showCalls = 0
            foreach ($id in $objectHistory.Keys) {
                if ($showCalls -ge $MaxHistoryContentCalls) { break }
                $path = "$PathPrefix/$id.sql"
                foreach ($version in $objectHistory[$id]) {
                    if ($showCalls -ge $MaxHistoryContentCalls) { break }
                    $showCalls++
                    try {
                        $content = & git -C $RepoRoot show "$($version.sha):$path" 2>$null
                        if ($LASTEXITCODE -eq 0 -and $content) {
                            $parsed = ConvertFrom-SyncSqlObjectFileLines -Lines @($content -split "`n")
                            $version.ddl = $parsed.Ddl
                        }
                    }
                    catch {
                        # Leave ddl = $null for this version (e.g. renamed/deleted at that
                        # revision) - the version still shows up in the timeline.
                    }
                }
                $nodesById[$id]['history'] = @($objectHistory[$id])
            }

            $coChangePairs = @($coChangeCounts.GetEnumerator() | Sort-Object -Property Value -Descending | Select-Object -First 100 | ForEach-Object {
                $ids = $_.Key -split '\|', 2
                [ordered]@{ a = $ids[0]; b = $ids[1]; count = $_.Value }
            })

            Write-SyncSqlLog "History mining complete: $($recentChanges.Count) relevant commit(s), $($coChangePairs.Count) co-change pair(s), $showCalls historical content fetch(es)"
        }
        catch {
            Write-SyncSqlLog "History mining failed (continuing without it): $($_.Exception.Message)" -Level WARN
            $recentChanges = @()
            $coChangePairs = @()
        }
    }
}

$catalog = [ordered]@{
    generatedAt    = (Get-Date).ToUniversalTime().ToString('o')
    servers        = @($nodes | ForEach-Object { $_.server } | Sort-Object -Unique)
    typeCounts     = $typeCounts
    nodes          = $nodes
    edges          = $edges
    recentChanges  = $recentChanges
    coChangePairs  = $coChangePairs
}

$outputDir = Split-Path -Parent $OutputPath
New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
$catalog | ConvertTo-Json -Depth 10 -Compress | Set-Content -LiteralPath $OutputPath -Encoding utf8

Write-SyncSqlLog "Wrote catalog to $OutputPath"
