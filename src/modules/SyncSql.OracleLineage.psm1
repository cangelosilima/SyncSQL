#Requires -Version 5.1
<#
    SyncSql.OracleLineage.psm1

    A real lexical scanner for Oracle DDL text, used to clean up the input
    Build-Catalog.ps1's existing regex-based qualified/bare-name and
    alias/column matching runs against, for Oracle nodes specifically.

    This is deliberately NOT a PL/SQL grammar/parser (see README's lineage
    section for why: the official ANTLR grammar generates ~300k lines of
    C#, with no prebuilt package to fetch instead, and no way to verify it
    even compiles under Windows PowerShell 5.1/.NET Framework without a
    real Windows host to test on - left as a follow-up once that can be
    done on an actual runner). What this DOES fix is the single biggest
    source of wrong matches in the old approach: a regex has no concept of
    "inside a string literal" or "inside a comment", so identifier-shaped
    text inside either one was scanned exactly like real SQL. A linear
    character scan can track that correctly (with proper '' / "" escape
    handling, unlike a regex-based attempt at the same thing), including
    Oracle's q'...' alternative quoting - a construct the old regex had no
    way to recognize at all, so a q'[SELECT * FROM foo]' literal would
    previously have "foo" scanned as if it were a real FROM-clause
    reference.

    Get-SyncSqlOracleScrubbedText blanks out comments and string literals
    (replacing their content with spaces, preserving newlines so line
    numbers stay meaningful for debugging) and unwraps "quoted identifiers"
    to their bare text (a real identifier reference, just written with
    explicit case-sensitive quoting) - then the caller runs the same
    qualified/bare-name and alias/column regexes Build-Catalog.ps1 already
    has against that cleaned text instead of the raw DDL.
#>

Set-StrictMode -Version Latest

function Get-SyncSqlOracleScrubbedText {
    <#
        Single left-to-right scan over $Ddl. Every character is either
        copied through unchanged, replaced with a space (comment/string
        literal content - kept out of later identifier matching), or
        substituted (quoted-identifier delimiters are dropped, keeping
        only the identifier text between them). Output is always the same
        length in lines as the input, so this is safe to feed to the
        existing regex passes with no other changes on their part.
    #>
    [CmdletBinding()]
    param([Parameter(Mandatory)][AllowEmptyString()][string]$Ddl)

    if ([string]::IsNullOrEmpty($Ddl)) { return $Ddl }

    $chars = $Ddl.ToCharArray()
    $len = $chars.Length
    $out = [System.Text.StringBuilder]::new($len)
    $i = 0

    while ($i -lt $len) {
        $c = $chars[$i]
        $nextChar = if (($i + 1) -lt $len) { $chars[$i + 1] } else { $null }

        if ($c -eq '-' -and $nextChar -eq '-') {
            # -- line comment, through end of line (line kept, content blanked)
            while ($i -lt $len -and $chars[$i] -ne "`n") {
                [void]$out.Append(' ')
                $i++
            }
            continue
        }

        if ($c -eq '/' -and $nextChar -eq '*') {
            # /* block comment */ - not nested, standard SQL semantics
            [void]$out.Append('  ')
            $i += 2
            while ($i -lt $len -and -not ($chars[$i] -eq '*' -and (($i + 1) -lt $len) -and $chars[$i + 1] -eq '/')) {
                [void]$out.Append($(if ($chars[$i] -eq "`n") { "`n" } else { ' ' }))
                $i++
            }
            if ($i -lt $len) {
                [void]$out.Append('  ')
                $i += 2
            }
            continue
        }

        $isQPrefix = ($c -eq 'q' -or $c -eq 'Q') -and ($nextChar -eq "'")
        if ($isQPrefix) {
            $prevIsIdentChar = $false
            if ($i -gt 0) {
                $prevIsIdentChar = ([char]::IsLetterOrDigit($chars[$i - 1])) -or ($chars[$i - 1] -eq '_')
            }
            $isQPrefix = -not $prevIsIdentChar
        }
        if ($isQPrefix -and (($i + 2) -lt $len)) {
            # Oracle alternative-quoted string literal: q'<open>...<close>'
            # where <open>/<close> is a bracket pair ([]/{}/()/<>) or, for
            # any other delimiter character, the same character on both ends.
            $openChar = $chars[$i + 2]
            $closeChar = switch ($openChar) {
                '[' { ']' }
                '{' { '}' }
                '(' { ')' }
                '<' { '>' }
                default { $openChar }
            }

            $j = $i + 3
            $closed = $false
            while ($j -lt $len) {
                if ($chars[$j] -eq $closeChar -and (($j + 1) -lt $len) -and $chars[$j + 1] -eq "'") {
                    $closed = $true
                    break
                }
                $j++
            }

            if ($closed) {
                for ($k = $i; $k -lt $j + 2; $k++) {
                    [void]$out.Append($(if ($chars[$k] -eq "`n") { "`n" } else { ' ' }))
                }
                $i = $j + 2
                continue
            }
            # Unterminated (shouldn't happen in valid DDL) - fall through and
            # treat the 'q'/'Q' as an ordinary character instead of guessing.
        }

        if ($c -eq "'") {
            # '...' string literal, '' = escaped literal quote
            [void]$out.Append(' ')
            $i++
            while ($i -lt $len) {
                if ($chars[$i] -eq "'") {
                    if ((($i + 1) -lt $len) -and $chars[$i + 1] -eq "'") {
                        [void]$out.Append('  ')
                        $i += 2
                        continue
                    }
                    [void]$out.Append(' ')
                    $i++
                    break
                }
                [void]$out.Append($(if ($chars[$i] -eq "`n") { "`n" } else { ' ' }))
                $i++
            }
            continue
        }

        if ($c -eq '"') {
            # "..." quoted identifier, "" = escaped literal quote - kept as
            # bare identifier text since it's a real reference, not a
            # literal to blank out. Delimiters are dropped rather than
            # replaced with spaces (unlike every other branch here) so
            # "Schema"."Table" comes out as the adjacent Schema.Table the
            # qualified-name regex expects - a space in that gap would
            # split it into two unrelated bare-name matches instead.
            $i++
            while ($i -lt $len) {
                if ($chars[$i] -eq '"') {
                    if ((($i + 1) -lt $len) -and $chars[$i + 1] -eq '"') {
                        [void]$out.Append('"')
                        $i += 2
                        continue
                    }
                    $i++
                    break
                }
                [void]$out.Append($chars[$i])
                $i++
            }
            continue
        }

        [void]$out.Append($c)
        $i++
    }

    return $out.ToString()
}

Export-ModuleMember -Function @(
    'Get-SyncSqlOracleScrubbedText'
)
