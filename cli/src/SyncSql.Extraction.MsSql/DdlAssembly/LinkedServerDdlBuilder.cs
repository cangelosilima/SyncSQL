namespace SyncSql.Extraction.MsSql.DdlAssembly;

/// <summary>A direct port of Get-SyncSqlMsSqlLinkedServers' foreach body. Passwords are never extractable from the catalog - the generated login-mapping script has a placeholder that must be filled in manually.</summary>
internal static class LinkedServerDdlBuilder
{
    public static string Build(
        string name,
        string? product,
        string? provider,
        string? dataSource,
        string? providerString,
        string? catalog,
        IReadOnlyList<(string? RemoteLoginName, bool? UsesSelfCredential)> logins)
    {
        List<string> lines =
        [
            "EXEC sp_addlinkedserver",
            $"    @server = N'{Quote(name)}',",
            $"    @srvproduct = N'{Quote(product)}',",
            $"    @provider = N'{Quote(provider)}',",
            $"    @datasrc = N'{Quote(dataSource)}',",
            $"    @provstr = N'{Quote(providerString)}',",
            $"    @catalog = N'{Quote(catalog)}';",
            "GO",
        ];

        foreach ((string? remoteLoginName, bool? usesSelfCredential) in logins)
        {
            if (string.IsNullOrWhiteSpace(remoteLoginName))
            {
                continue;
            }

            string useSelf = usesSelfCredential == true ? "TRUE" : "FALSE";
            lines.Add("-- Remote login mapping (password not extracted; re-set manually after restore):");
            lines.Add($"EXEC sp_addlinkedsrvlogin @rmtsrvname = N'{Quote(name)}', @useself = N'{useSelf}', @rmtuser = N'{Quote(remoteLoginName)}', @rmtpassword = N'########';");
        }

        return string.Join('\n', lines);
    }

    /// <summary>T-SQL string-literal escaping: a single quote inside an N'...' literal is doubled.</summary>
    private static string? Quote(string? value) => value?.Replace("'", "''", StringComparison.Ordinal);
}
