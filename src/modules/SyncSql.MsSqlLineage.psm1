#Requires -Version 5.1
<#
    SyncSql.MsSqlLineage.psm1

    Real T-SQL lineage inference for MSSQL objects, replacing the
    regex-over-text approach Build-Catalog.ps1 used to apply to every
    engine uniformly. Uses Microsoft.SqlServer.TransactSql.ScriptDom (MIT
    licensed, zero dependencies - see Bootstrap-Dependencies.ps1) to parse
    each object's DDL into a real AST and walk it for:

      - table/view references (FROM/JOIN/INTO/UPDATE/DELETE targets, via
        NamedTableReference)
      - FK REFERENCES targets (ForeignKeyConstraintDefinition.ReferenceTableName
        - the appended, structurally-extracted Foreign Keys section is real
        T-SQL, scanned alongside the object's own DDL)
      - schema-qualified function calls (FunctionCall.CallTarget)
      - EXEC/EXECUTE procedure references (ExecuteStatement)
      - column references bound to their FROM-clause alias
        (ColumnReferenceExpression), so column-level lineage tagging no
        longer depends on regex-guessing which alias a "prefix.column"
        token refers to.

    This still can't see dynamic SQL (a string being built and EXEC'd) or
    cross-linked-server four-part names beyond the immediate reference -
    same inherent limits as any static analysis - but it no longer matches
    identifiers inside string literals/comments, and no longer misreads
    SELECT * or computed columns as identifiers, because a real parser
    doesn't tokenize those as identifiers in the first place.

    A small C# visitor (SyncSql.Lineage.TSqlLineageVisitor) is compiled at
    runtime via Add-Type -TypeDefinition once ScriptDom.dll is loaded -
    not a PowerShell class inheriting the ScriptDom visitor type, because
    a `class ... : SomeType` statement at module scope would require the
    assembly to already be loaded just to *import* this module, which
    would break the "everything optional degrades independently" posture
    the rest of this project follows (ScriptDom might not be bootstrapped
    yet, or this function might simply never be called). Deliberately
    written in a conservative C# subset (no `var`, no pattern matching,
    no object initializers) since Add-Type's compiler on a given Windows
    host can't be assumed to support anything newer than C# 3/4.
#>

Set-StrictMode -Version Latest

$script:SyncSqlScriptDomAssembly = $null
$script:SyncSqlScriptDomParser = $null

function Find-SyncSqlScriptDomDll {
    <#
        Searches -CacheDir (Bootstrap-Dependencies.ps1's scriptdom staging
        area) for the .NET-Framework build of ScriptDom. The package ships
        a real per-TFM lib/ layout, so this looks for lib/net472 (or any
        other net4x folder, in case a future package version renumbers
        it) specifically rather than net8.0/netstandard - Windows
        PowerShell 5.1 runs on .NET Framework, not .NET Core/Standard.
    #>
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$CacheDir)

    if (-not (Test-Path -LiteralPath $CacheDir)) { return $null }

    $candidates = Get-ChildItem -LiteralPath $CacheDir -Recurse -Filter 'Microsoft.SqlServer.TransactSql.ScriptDom.dll' -ErrorAction SilentlyContinue
    if (-not $candidates) { return $null }

    $preferred = $candidates | Where-Object { $_.FullName -match '[\\/]net4\d+[\\/]' } | Select-Object -First 1
    if ($preferred) { return $preferred.FullName }

    return ($candidates | Select-Object -First 1).FullName
}

# Kept as a single string (rather than several -join'd lines) so the
# compiled type's source is exactly what a reader sees here - no
# reconstruction step to double-check.
$script:SyncSqlLineageVisitorSource = @'
using System;
using System.Collections.Generic;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SyncSql.Lineage
{
    public class ObjectRef
    {
        public string Schema;
        public string Name;
    }

    public class ColumnRef
    {
        public string AliasOrTable;
        public string Column;
    }

    public class TSqlLineageVisitor : TSqlFragmentVisitor
    {
        public List<ObjectRef> ObjectRefs = new List<ObjectRef>();
        public Dictionary<string, ObjectRef> Aliases = new Dictionary<string, ObjectRef>(StringComparer.OrdinalIgnoreCase);
        public List<ColumnRef> ColumnRefs = new List<ColumnRef>();

        private static ObjectRef FromSchemaObjectName(SchemaObjectName name)
        {
            if (name == null || name.BaseIdentifier == null) { return null; }
            ObjectRef result = new ObjectRef();
            result.Name = name.BaseIdentifier.Value;
            result.Schema = (name.SchemaIdentifier != null) ? name.SchemaIdentifier.Value : null;
            return result;
        }

        // FROM/JOIN/INTO/UPDATE/DELETE targets - anything ScriptDom
        // represents as a plain named table/view reference.
        public override void Visit(NamedTableReference node)
        {
            ObjectRef objRef = FromSchemaObjectName(node.SchemaObject);
            if (objRef == null) { return; }
            ObjectRefs.Add(objRef);

            if (node.Alias != null && node.Alias.Value != null)
            {
                Aliases[node.Alias.Value] = objRef;
            }
            // Also index by the object's own (unaliased) name/base identifier,
            // so "dbo.Orders.OrderId" or a bare "Orders.OrderId" column
            // reference still resolves without requiring an explicit alias.
            if (!Aliases.ContainsKey(objRef.Name))
            {
                Aliases[objRef.Name] = objRef;
            }
        }

        // Schema-qualified scalar/table-valued function calls
        // (dbo.MyFunc(...)); unqualified calls (MyFunc(...)) are
        // indistinguishable from built-in function calls at the AST level
        // without a full catalog, so those are intentionally left alone -
        // same "don't guess" posture the bare-name resolver already has.
        public override void Visit(FunctionCall node)
        {
            // CallTarget holds only the qualifying prefix (e.g. "dbo" in
            // dbo.MyFunc(...)) - the function's own name is the separate
            // FunctionName property, NOT the last element of
            // CallTarget.MultiPartIdentifier.Identifiers. (Verified against
            // the real assembly: a naive "last identifier = name" reading -
            // the same shape SchemaObjectName uses - silently produces the
            // wrong ObjectRef here, since FunctionCall's CallTarget model is
            // different from NamedTableReference's SchemaObject model.)
            if (node.FunctionName == null || node.FunctionName.Value == null) { return; }
            MultiPartIdentifierCallTarget target = node.CallTarget as MultiPartIdentifierCallTarget;
            if (target == null || target.MultiPartIdentifier == null) { return; }
            IList<Identifier> ids = target.MultiPartIdentifier.Identifiers;
            if (ids.Count < 1) { return; }

            ObjectRef objRef = new ObjectRef();
            objRef.Name = node.FunctionName.Value;
            objRef.Schema = ids[ids.Count - 1].Value;
            ObjectRefs.Add(objRef);
        }

        // ALTER TABLE ... ADD CONSTRAINT ... FOREIGN KEY ... REFERENCES
        // other_table (...) - the appended, structurally-extracted Foreign
        // Keys section is real T-SQL (see SyncSql.MsSql.psm1's
        // Get-SyncSqlMsSqlForeignKeys), scanned alongside the object's own
        // DDL. REFERENCES targets are a SchemaObjectName directly on the
        // constraint definition, not a NamedTableReference, so this needs
        // its own override.
        public override void Visit(ForeignKeyConstraintDefinition node)
        {
            ObjectRef objRef = FromSchemaObjectName(node.ReferenceTableName);
            if (objRef != null) { ObjectRefs.Add(objRef); }
        }

        // EXEC/EXECUTE dbo.MyProc ...
        public override void Visit(ExecuteStatement node)
        {
            if (node.ExecuteSpecification == null) { return; }
            ExecutableProcedureReference procRef = node.ExecuteSpecification.ExecutableEntity as ExecutableProcedureReference;
            if (procRef == null || procRef.ProcedureReference == null || procRef.ProcedureReference.ProcedureReference == null) { return; }

            ObjectRef objRef = FromSchemaObjectName(procRef.ProcedureReference.ProcedureReference.Name);
            if (objRef != null) { ObjectRefs.Add(objRef); }
        }

        // alias.column / table.column references - only multi-part ones
        // are useful for column-level tagging (a bare column name can't
        // be attributed to a specific source without full binder-level
        // type resolution, which ScriptDom deliberately doesn't do).
        public override void Visit(ColumnReferenceExpression node)
        {
            if (node.MultiPartIdentifier == null) { return; }
            IList<Identifier> ids = node.MultiPartIdentifier.Identifiers;
            if (ids.Count < 2) { return; }

            ColumnRef colRef = new ColumnRef();
            colRef.AliasOrTable = ids[ids.Count - 2].Value;
            colRef.Column = ids[ids.Count - 1].Value;
            ColumnRefs.Add(colRef);
        }
    }
}
'@

function Import-SyncSqlScriptDom {
    <#
        Loads ScriptDom.dll (if not already loaded) and compiles the
        TSqlLineageVisitor helper type against it (if not already
        compiled). Safe to call repeatedly/from multiple functions.
    #>
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$DllPath)

    if (-not (Test-Path -LiteralPath $DllPath)) {
        throw "ScriptDom not found at '$DllPath'. Run Bootstrap-Dependencies.ps1 first."
    }

    if (-not ([System.Management.Automation.PSTypeName]'Microsoft.SqlServer.TransactSql.ScriptDom.TSqlFragmentVisitor').Type) {
        Add-Type -Path $DllPath -ErrorAction Stop
    }
    $script:SyncSqlScriptDomAssembly = $DllPath

    if (-not ([System.Management.Automation.PSTypeName]'SyncSql.Lineage.TSqlLineageVisitor').Type) {
        Add-Type -TypeDefinition $script:SyncSqlLineageVisitorSource -ReferencedAssemblies $DllPath -ErrorAction Stop
    }
}

function Get-SyncSqlMsSqlParser {
    <#
        Returns a cached parser instance for the newest TSqlNNNParser type
        the loaded ScriptDom assembly exposes, found via reflection rather
        than a hardcoded class name (Microsoft adds a new TSqlNNNParser
        roughly per SQL Server release; hardcoding one would silently stop
        picking up newer syntax support on a ScriptDom upgrade instead of
        just working). $true = quoted identifiers on, matching this
        project's extracted DDL (identifiers are bracket-quoted).
    #>
    if ($script:SyncSqlScriptDomParser) { return $script:SyncSqlScriptDomParser }
    if (-not $script:SyncSqlScriptDomAssembly) {
        throw 'Import-SyncSqlScriptDom must be called before Get-SyncSqlMsSqlParser.'
    }

    $asm = [Microsoft.SqlServer.TransactSql.ScriptDom.TSqlFragmentVisitor].Assembly
    $parserType = $asm.GetTypes() | Where-Object { $_.Name -match '^TSql(\d+)Parser$' -and $_.IsPublic } |
        Sort-Object -Property { [int]([regex]::Match($_.Name, '\d+').Value) } -Descending |
        Select-Object -First 1

    if (-not $parserType) {
        throw 'Could not find any TSqlNNNParser type in the loaded ScriptDom assembly.'
    }

    $script:SyncSqlScriptDomParser = [Activator]::CreateInstance($parserType, @($true))
    return $script:SyncSqlScriptDomParser
}

function Get-SyncSqlMsSqlDdlReferences {
    <#
        Parses $Ddl (one object's DDL text, or DDL + the structural
        Foreign Keys section appended to it) and returns:
          ObjectRefs  - @([pscustomobject]@{Schema=...; Name=...}, ...)
                        every table/view/function/proc reference found.
          Aliases     - hashtable, FROM-clause alias (or the object's own
                        unaliased name) -> the ObjectRef it resolves to.
          ColumnRefs  - @([pscustomobject]@{AliasOrTable=...; Column=...})
                        every "prefix.column" reference found.

        Degrades to an all-empty result (with a warning) on any parse
        failure rather than throwing - same "optional step, never blocks
        the rest of the catalog build" posture as the rest of this
        project. A non-empty error list from the parser doesn't
        necessarily mean nothing useful was extracted - ScriptDom does
        error-recovery parsing - so this still walks whatever fragment
        tree comes back rather than treating errors as fatal.
    #>
    [CmdletBinding()]
    param([Parameter(Mandatory)][AllowEmptyString()][string]$Ddl)

    $empty = [pscustomobject]@{
        ObjectRefs = @()
        Aliases    = @{}
        ColumnRefs = @()
    }
    if ([string]::IsNullOrWhiteSpace($Ddl)) { return $empty }

    try {
        $parser = Get-SyncSqlMsSqlParser
        $reader = [System.IO.StringReader]::new($Ddl)
        try {
            $parseErrors = $null
            $fragment = $parser.Parse($reader, [ref]$parseErrors)
        }
        finally {
            $reader.Dispose()
        }

        if (-not $fragment) { return $empty }
        if ($parseErrors -and $parseErrors.Count -gt 0) {
            Write-SyncSqlLog "ScriptDom parse produced $($parseErrors.Count) error(s) (continuing with the partial AST): $($parseErrors[0].Message)" -Level WARN
        }

        $visitor = New-Object 'SyncSql.Lineage.TSqlLineageVisitor'
        $fragment.Accept($visitor)

        return [pscustomobject]@{
            ObjectRefs = @($visitor.ObjectRefs | ForEach-Object { [pscustomobject]@{ Schema = $_.Schema; Name = $_.Name } })
            Aliases    = $visitor.Aliases
            ColumnRefs = @($visitor.ColumnRefs | ForEach-Object { [pscustomobject]@{ AliasOrTable = $_.AliasOrTable; Column = $_.Column } })
        }
    }
    catch {
        Write-SyncSqlLog "ScriptDom parsing failed (skipping lineage for this object): $($_.Exception.Message)" -Level WARN
        return $empty
    }
}

Export-ModuleMember -Function @(
    'Find-SyncSqlScriptDomDll',
    'Import-SyncSqlScriptDom',
    'Get-SyncSqlMsSqlParser',
    'Get-SyncSqlMsSqlDdlReferences'
)
