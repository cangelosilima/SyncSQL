namespace SyncSql.Extraction.Oracle;

/// <summary>Every dictionary-view query the Oracle extractor runs, verbatim from SyncSql.Oracle.psm1 (`:name` bind variables - see OracleCommandRunner for why BindByName=true matters here).</summary>
internal static class OracleQueries
{
    public const string Schemas = "SELECT DISTINCT OWNER FROM ALL_OBJECTS ORDER BY OWNER";

    // PUBLIC is not a user. NOT EXISTS keeps application-owned public synonyms/links visible.
    public const string ApplicationSchemas = """
        SELECT DISTINCT o.OWNER FROM ALL_OBJECTS o
        WHERE o.ORACLE_MAINTAINED = 'N'
          AND NOT EXISTS (SELECT 1 FROM ALL_USERS u WHERE u.USERNAME = o.OWNER AND u.ORACLE_MAINTAINED = 'Y')
        ORDER BY o.OWNER
        """;

    // Nested storage and IOT auxiliary tables are emitted with their parent table's DDL.
    // SYS_YOID<n>$ types are internal XML object identifiers, not standalone user types.
    // Apply these GET_DDL eligibility checks even when default exclusions are disabled.
    private const string StandaloneDdlObjects = """
          AND (o.object_type <> 'TABLE' OR EXISTS (
              SELECT 1 FROM ALL_ALL_TABLES t
              WHERE t.owner = o.owner AND t.table_name = o.object_name
                AND t.nested = 'NO' AND (t.iot_type IS NULL OR t.iot_type = 'IOT')))
          AND (o.object_type NOT IN ('TYPE', 'TYPE BODY')
               OR NOT REGEXP_LIKE(o.object_name, '^SYS_YOID[0-9]+\$$', 'c'))
        """;

    public const string ObjectList = """
        SELECT object_name AS ObjectName
        FROM ALL_OBJECTS o
        WHERE owner = :owner
          AND object_type = :objType
          AND generated = 'N'
          AND temporary = 'N'
        """ + "\n" + StandaloneDdlObjects + "\nORDER BY object_name";

    public const string ApplicationObjectList = """
        SELECT object_name AS ObjectName FROM ALL_OBJECTS o
        WHERE owner = :owner AND object_type = :objType
          AND generated = 'N' AND temporary = 'N'
          AND oracle_maintained = 'N' AND secondary = 'N'
        """ + "\n" + StandaloneDdlObjects + "\nORDER BY object_name";

    public const string LegacyApplicationObjectList = """
        SELECT object_name AS ObjectName FROM ALL_OBJECTS o
        WHERE owner = :owner AND object_type = :objType
          AND generated = 'N' AND temporary = 'N' AND secondary = 'N'
        """ + "\n" + StandaloneDdlObjects + "\nORDER BY object_name";

    public const string DatabaseLinks = "SELECT OWNER, DB_LINK, USERNAME, HOST FROM ALL_DB_LINKS ORDER BY OWNER, DB_LINK";
    public const string AllDatabaseLinks = "SELECT OWNER, DB_LINK, USERNAME, HOST FROM DBA_DB_LINKS ORDER BY OWNER, DB_LINK";

    public const string ApplicationDatabaseLinks = """
        SELECT OWNER, DB_LINK, USERNAME, HOST FROM ALL_DB_LINKS l
        WHERE NOT EXISTS (SELECT 1 FROM ALL_USERS u WHERE u.USERNAME = l.OWNER AND u.ORACLE_MAINTAINED = 'Y')
        ORDER BY OWNER, DB_LINK
        """;
    public const string AllApplicationDatabaseLinks = """
        SELECT OWNER, DB_LINK, USERNAME, HOST FROM DBA_DB_LINKS l
        WHERE NOT EXISTS (SELECT 1 FROM ALL_USERS u WHERE u.USERNAME = l.OWNER AND u.ORACLE_MAINTAINED = 'Y')
        ORDER BY OWNER, DB_LINK
        """;

    // ALL_TAB_PRIVS/ALL_COL_PRIVS name their owning-schema column TABLE_SCHEMA (OWNER only exists on
    // the DBA_*/`_MADE`/`_RECD` variants of these views).
    public const string ObjectGrants = "SELECT GRANTEE, TABLE_NAME, PRIVILEGE FROM ALL_TAB_PRIVS WHERE TABLE_SCHEMA = :owner ORDER BY TABLE_NAME, GRANTEE, PRIVILEGE";
    public const string AllObjectGrants = "SELECT GRANTEE, TABLE_NAME, PRIVILEGE FROM DBA_TAB_PRIVS WHERE OWNER = :owner ORDER BY TABLE_NAME, GRANTEE, PRIVILEGE";

    public const string ColumnGrants = "SELECT GRANTEE, TABLE_NAME, COLUMN_NAME, PRIVILEGE FROM ALL_COL_PRIVS WHERE TABLE_SCHEMA = :owner ORDER BY TABLE_NAME, GRANTEE, PRIVILEGE";
    public const string AllColumnGrants = "SELECT GRANTEE, TABLE_NAME, COLUMN_NAME, PRIVILEGE FROM DBA_COL_PRIVS WHERE OWNER = :owner ORDER BY TABLE_NAME, COLUMN_NAME, GRANTEE, PRIVILEGE";

    public const string ColumnList = "SELECT TABLE_NAME, COLUMN_NAME, DATA_TYPE, COLUMN_ID FROM ALL_TAB_COLUMNS WHERE OWNER = :owner ORDER BY TABLE_NAME, COLUMN_ID";

    /// <summary>Estimated size from BLOCKS * 8KB (assuming the common 8K block size) rather than DBA_SEGMENTS, to avoid needing elevated privileges.</summary>
    public const string TableStatistics = """
        SELECT TABLE_NAME, NUM_ROWS, BLOCKS, SAMPLE_SIZE, LAST_ANALYZED
        FROM ALL_TAB_STATISTICS
        WHERE OWNER = :owner AND PARTITION_NAME IS NULL AND SUBPARTITION_NAME IS NULL AND OBJECT_TYPE = 'TABLE'
        """;

    public const string TableModifications = "SELECT TABLE_NAME, INSERTS, UPDATES, DELETES FROM ALL_TAB_MODIFICATIONS WHERE TABLE_OWNER = :owner";

    public const string IndexStatistics = """
        SELECT INDEX_NAME, TABLE_NAME, NUM_ROWS, DISTINCT_KEYS, LEAF_BLOCKS, LAST_ANALYZED
        FROM ALL_IND_STATISTICS
        WHERE TABLE_OWNER = :owner AND PARTITION_NAME IS NULL AND SUBPARTITION_NAME IS NULL
        """;

    public const string GetDdl = "SELECT DBMS_METADATA.GET_DDL(:objType, :objName, :owner) FROM DUAL";
}
