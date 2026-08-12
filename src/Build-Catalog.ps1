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

.PARAMETER ObjectsRoot
    Root of the extracted tree (Export-DatabaseObjects.ps1's -StagingRoot).

.PARAMETER OutputPath
    File path the catalog JSON is written to.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ObjectsRoot,
    [Parameter(Mandatory)][string]$OutputPath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

Import-Module (Join-Path $PSScriptRoot 'modules/SyncSql.Common.psm1') -Force

if (-not (Test-Path -LiteralPath $ObjectsRoot)) {
    throw "Objects root not found: $ObjectsRoot"
}

function ConvertFrom-SyncSqlObjectFile {
    param([Parameter(Mandatory)][string]$FilePath)

    $allLines = @(Get-Content -LiteralPath $FilePath)
    $i = 0
    while ($i -lt $allLines.Count -and $allLines[$i].StartsWith('-- ')) { $i++ }
    if ($i -lt $allLines.Count -and [string]::IsNullOrEmpty($allLines[$i])) { $i++ }

    # NOTE: @() must wrap the *whole* if/else statement, not each branch -
    # PowerShell unrolls a script block's output stream when collecting it
    # into a variable, so a single-element array returned from a branch
    # collapses back into a scalar unless the enclosing statement is itself
    # wrapped in @().
    $bodyLines = @(if ($i -lt $allLines.Count) { $allLines[$i..($allLines.Count - 1)] } else { @() })

    $markerIndex = -1
    for ($j = 0; $j -lt $bodyLines.Count; $j++) {
        if ($bodyLines[$j] -eq '-- === Extended Properties ===') { $markerIndex = $j; break }
    }

    if ($markerIndex -ge 0) {
        $ddlLines = @(if ($markerIndex -gt 0) { $bodyLines[0..($markerIndex - 1)] } else { @() })
        $propLines = @(if ($markerIndex + 1 -lt $bodyLines.Count) { $bodyLines[($markerIndex + 1)..($bodyLines.Count - 1)] } else { @() })
    }
    else {
        $ddlLines = $bodyLines
        $propLines = @()
    }

    $objectDescription = $null
    $columns = [ordered]@{}
    foreach ($line in $propLines) {
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

    return [pscustomobject]@{
        Ddl          = ($ddlLines -join "`n").Trim()
        Description  = $objectDescription
        Columns      = $columns
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
        columns       = @($parsed.Columns.Keys | ForEach-Object { [ordered]@{ name = $_; description = $parsed.Columns[$_] } })
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
    if ([string]::IsNullOrWhiteSpace($node.ddl)) { continue }

    foreach ($match in $qualifiedPattern.Matches($node.ddl)) {
        $schemaLower = $match.Groups[1].Value.ToLowerInvariant()
        $nameLower = $match.Groups[2].Value.ToLowerInvariant()
        $key = "$($node.server)::$($node.database)::$schemaLower.$nameLower"
        if ($qualifiedIndex.ContainsKey($key)) {
            Add-SyncSqlEdge -From $node.id -To $qualifiedIndex[$key]
        }
    }

    $seenBareTokens = [System.Collections.Generic.HashSet[string]]::new()
    foreach ($match in $barePattern.Matches($node.ddl)) {
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

Write-SyncSqlLog "Built $($nodes.Count) node(s), $($edges.Count) edge(s)"

$typeCounts = [ordered]@{}
foreach ($node in $nodes) {
    if (-not $typeCounts.Contains($node.type)) { $typeCounts[$node.type] = 0 }
    $typeCounts[$node.type]++
}

$catalog = [ordered]@{
    generatedAt = (Get-Date).ToUniversalTime().ToString('o')
    servers     = @($nodes | ForEach-Object { $_.server } | Sort-Object -Unique)
    typeCounts  = $typeCounts
    nodes       = $nodes
    edges       = $edges
}

$outputDir = Split-Path -Parent $OutputPath
New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
$catalog | ConvertTo-Json -Depth 10 -Compress | Set-Content -LiteralPath $OutputPath -Encoding utf8

Write-SyncSqlLog "Wrote catalog to $OutputPath"
