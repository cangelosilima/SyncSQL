using SyncSql.Core.Domain;

namespace SyncSql.Catalog;

/// <summary>
/// Recognizes references to objects the database engine itself provides, so they stop being mistaken for
/// dropped ones.
///
/// Nothing extracts <c>sp_executesql</c>, <c>sys.objects</c> or <c>DBMS_OUTPUT</c> - they aren't user
/// objects, and the default extraction config excludes the <c>sys</c> schema outright. Before this
/// existed, whether such a reference was flagged as an orphan came down to an accident: a bare
/// <c>sp_executesql</c> resolved nowhere and became one, while <c>master.dbo.xp_cmdshell</c> escaped only
/// because <c>master</c> usually isn't extracted either. That is noise on top of the one list that is
/// supposed to be actionable.
///
/// The tests here are deliberately conservative - engine-reserved prefixes and schemas only, never a
/// general "looks built-in" guess - and, crucially, they are consulted only *after* the normal lookup has
/// failed (see <see cref="NodeIndex"/>). A user-written <c>dbo.sp_MyHelper</c> that really is in the
/// catalog resolves to itself as it always did; the prefix rules only ever decide what to do with a name
/// nothing answers to.
/// </summary>
internal static class SystemObjectCatalog
{
    /// <summary>
    /// Schemas SQL Server owns. <c>sys</c> and <c>INFORMATION_SCHEMA</c> are exactly what the shipped
    /// extraction config excludes, so a reference into them can never resolve however much is extracted.
    /// </summary>
    private static readonly string[] MsSqlSystemSchemas = ["sys", "INFORMATION_SCHEMA"];

    /// <summary>
    /// The databases SQL Server ships. A name that spells one of these out and resolves nowhere is
    /// reaching for a built-in, not for something that used to exist here.
    /// </summary>
    private static readonly string[] MsSqlSystemDatabases = ["master", "msdb", "tempdb", "model"];

    /// <summary>
    /// Prefixes SQL Server reserves for its own routines, and - this is the part that matters - resolves
    /// out of <c>master</c> automatically no matter which database you call them from. That auto-resolution
    /// is why an unqualified <c>sp_executesql</c> is a perfectly ordinary call rather than a broken one.
    /// </summary>
    private static readonly string[] MsSqlSystemNamePrefixes = ["sp_", "xp_", "fn_", "dm_", "dt_", "sys."];

    /// <summary>The schemas an unqualified system routine is habitually written under when it is qualified at all.</summary>
    private static readonly string[] MsSqlSystemCallSchemas = ["dbo", "sys"];

    /// <summary>Schemas/owners Oracle owns.</summary>
    private static readonly string[] OracleSystemSchemas = ["SYS", "SYSTEM", "PUBLIC", "SYSAUX", "DBSNMP"];

    /// <summary>Oracle's built-in package and data-dictionary prefixes.</summary>
    private static readonly string[] OracleSystemNamePrefixes = ["DBMS_", "UTL_", "DBA_", "ALL_", "USER_", "V$", "GV$", "OWA_", "HTP", "HTF"];

    /// <summary>
    /// True when this reference names something the engine provides. <paramref name="engine"/> is the
    /// referencing object's own engine - the reference is written in that dialect, so that is the rule set
    /// that applies. A node with no engine tag (a file predating the header) gets no built-in treatment,
    /// matching how lineage inference already skips it rather than guessing.
    /// </summary>
    public static bool IsSystemObject(DatabaseEngine? engine, ObjectRef reference) => engine switch
    {
        DatabaseEngine.MsSql => IsMsSqlSystemObject(reference),
        DatabaseEngine.Oracle => IsOracleSystemObject(reference),
        _ => false,
    };

    private static bool IsMsSqlSystemObject(ObjectRef reference)
    {
        if (Matches(reference.Schema, MsSqlSystemSchemas))
        {
            return true;
        }

        // A spelled-out system database only counts together with a system-looking name: "master.dbo.Foo"
        // is somebody's own table that happens to live in master, and calling that a built-in would hide a
        // real dangling reference.
        bool systemName = StartsWithAny(reference.Name, MsSqlSystemNamePrefixes);
        if (Matches(reference.Database, MsSqlSystemDatabases))
        {
            return systemName || string.IsNullOrWhiteSpace(reference.Schema);
        }

        // Unqualified, or qualified with the one schema these are habitually written with. A prefixed name
        // under some other schema ("app.sp_Nightly") is a user object using the prefix as a convention, and
        // if it is missing that is worth knowing about.
        return systemName && (string.IsNullOrWhiteSpace(reference.Schema) || Matches(reference.Schema, MsSqlSystemCallSchemas));
    }

    private static bool IsOracleSystemObject(ObjectRef reference) =>
        Matches(reference.Schema, OracleSystemSchemas) || StartsWithAny(reference.Name, OracleSystemNamePrefixes);

    private static bool Matches(string? value, string[] candidates) =>
        !string.IsNullOrWhiteSpace(value) && candidates.Contains(value, StringComparer.OrdinalIgnoreCase);

    private static bool StartsWithAny(string? value, string[] prefixes) =>
        !string.IsNullOrWhiteSpace(value) && prefixes.Any(prefix => value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
}
