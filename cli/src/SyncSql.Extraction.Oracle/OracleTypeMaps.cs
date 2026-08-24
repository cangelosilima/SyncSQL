namespace SyncSql.Extraction.Oracle;

internal static class OracleTypeMaps
{
    /// <summary>The config's objectTypes vocabulary -> ALL_OBJECTS.OBJECT_TYPE value. Order matters: it's the order object types are extracted in.</summary>
    public static readonly IReadOnlyList<(string ConfigType, string OracleObjectType)> ObjectTypeMap =
    [
        ("Tables", "TABLE"),
        ("Views", "VIEW"),
        ("Procedures", "PROCEDURE"),
        ("Functions", "FUNCTION"),
        ("Packages", "PACKAGE"),
        ("PackageBodies", "PACKAGE BODY"),
        ("Triggers", "TRIGGER"),
        ("Synonyms", "SYNONYM"),
    ];

    /// <summary>ALL_OBJECTS.OBJECT_TYPE value -> the type name DBMS_METADATA.GET_DDL expects (differs only for PACKAGE BODY).</summary>
    public static string ToDdlType(string oracleObjectType) => oracleObjectType == "PACKAGE BODY" ? "PACKAGE_BODY" : oracleObjectType;
}
