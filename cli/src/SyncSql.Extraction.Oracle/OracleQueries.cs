namespace SyncSql.Extraction.Oracle;

/// <summary>Every dictionary-view query the Oracle extractor runs, verbatim from SyncSql.Oracle.psm1 (`:name` bind variables - see OracleCommandRunner for why BindByName=true matters here).</summary>
internal static class OracleQueries
{
    public const string Schemas = "SELECT DISTINCT OWNER FROM ALL_OBJECTS ORDER BY OWNER";

    public const string ObjectList = """
        SELECT object_name AS ObjectName
        FROM ALL_OBJECTS
        WHERE owner = :owner
          AND object_type = :objType
          AND generated = 'N'
          AND temporary = 'N'
        ORDER BY object_name
        """;

    public const string DatabaseLinks = "SELECT OWNER, DB_LINK FROM ALL_DB_LINKS ORDER BY OWNER, DB_LINK";

    public const string ObjectGrants = "SELECT GRANTEE, TABLE_NAME, PRIVILEGE FROM ALL_TAB_PRIVS WHERE OWNER = :owner ORDER BY TABLE_NAME, GRANTEE, PRIVILEGE";

    public const string ColumnGrants = "SELECT GRANTEE, TABLE_NAME, COLUMN_NAME, PRIVILEGE FROM ALL_COL_PRIVS WHERE OWNER = :owner ORDER BY TABLE_NAME, GRANTEE, PRIVILEGE";

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
