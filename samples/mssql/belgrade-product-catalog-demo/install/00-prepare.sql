-- SyncSQL sample prelude - not part of microsoft/sql-server-samples.
--
-- Two things the upstream scripts assume about the database they land in, which a
-- freshly created one does not provide:
--   1. "1 setup.sql" opens with an unguarded DROP LOGIN WebLogin, so on a first run
--      the login has to already exist for the drop to succeed.
--   2. "7. xtp.sql" creates a MEMORY_OPTIMIZED table, which needs the database to
--      have a memory-optimized filegroup.
SET NOCOUNT ON;
GO

IF SUSER_ID(N'WebLogin') IS NULL
BEGIN
    -- Same password the upstream script re-creates it with, kept in step deliberately.
    CREATE LOGIN WebLogin WITH PASSWORD = 'SQLPass1234!';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.filegroups WHERE type = 'FX')
BEGIN
    DECLARE @db sysname = DB_NAME();
    DECLARE @sql nvarchar(max) =
        N'ALTER DATABASE ' + QUOTENAME(@db) + N' ADD FILEGROUP ' + QUOTENAME(@db + N'_xtp') + N' CONTAINS MEMORY_OPTIMIZED_DATA;';
    EXEC sys.sp_executesql @sql;

    SET @sql =
        N'ALTER DATABASE ' + QUOTENAME(@db) + N' ADD FILE (name = N''' + @db + N'_xtp'', filename = N''/var/opt/mssql/data/' + @db + N'_xtp'')' +
        N' TO FILEGROUP ' + QUOTENAME(@db + N'_xtp') + N';';
    EXEC sys.sp_executesql @sql;
END
GO
