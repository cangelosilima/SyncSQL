namespace SyncSql.Extraction.Oracle;

internal static class OracleTypeMaps
{
    /// <summary>The config's objectTypes vocabulary -> ALL_OBJECTS.OBJECT_TYPE value. Order matters: it's the order object types are extracted in.</summary>
    public static readonly IReadOnlyList<(string ConfigType, string OracleObjectType)> ObjectTypeMap =
    [
        ("Tables", "TABLE"),
        ("Types", "TYPE"),
        ("TypeBodies", "TYPE BODY"),
        ("Views", "VIEW"),
        ("Procedures", "PROCEDURE"),
        ("Functions", "FUNCTION"),
        ("Packages", "PACKAGE"),
        ("PackageBodies", "PACKAGE BODY"),
        ("Triggers", "TRIGGER"),
        ("Synonyms", "SYNONYM"),
    ];

    /// <summary>PACKAGE returns specification and body together; extract the specification separately so dependencies are attributed correctly.</summary>
    public static string ToDdlType(string oracleObjectType) =>
        oracleObjectType == "PACKAGE" ? "PACKAGE_SPEC" : oracleObjectType.Replace(' ', '_');
}
