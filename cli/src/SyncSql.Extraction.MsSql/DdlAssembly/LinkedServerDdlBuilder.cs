namespace SyncSql.Extraction.MsSql.DdlAssembly;

/// <summary>A direct port of Get-SyncSqlMsSqlLinkedServers' foreach body. Passwords are never extractable from the catalog - the generated login-mapping script has a placeholder that must be filled in manually.</summary>
internal static class LinkedServerDdlBuilder
{
    public static string Build(Sql.LinkedServerRow server, IReadOnlyList<Sql.LinkedServerRow> mappings)
    {
        string ddl = Build(server.LinkedServerName, server.Product, server.Provider, server.DataSource,
            server.ProviderString, server.Catalog, [], server.Location);
        List<string> lines = [ddl];
        // Creation adds a global self-mapping. Restore only mappings present in the source.
        lines.Add($"EXEC sp_droplinkedsrvlogin @rmtsrvname = {SqlText.Literal(server.LinkedServerName)}, @locallogin = NULL;");
        foreach (Sql.LinkedServerRow mapping in mappings.Where(m => m.UsesSelfCredential is not null))
        {
            string login = $"EXEC sp_addlinkedsrvlogin @rmtsrvname = {SqlText.Literal(server.LinkedServerName)}, " +
                $"@locallogin = {SqlText.Literal(mapping.LocalLoginName)}, @useself = N'{(mapping.UsesSelfCredential == true ? "TRUE" : "FALSE")}'";
            if (mapping.UsesSelfCredential == false)
            {
                lines.Add("-- Remote login mapping (password not extracted; re-set manually after restore):");
                login += $", @rmtuser = {SqlText.Literal(mapping.RemoteLoginName)}, @rmtpassword = N'########'";
            }
            lines.Add(login + ";");
        }
        (string Name, string Value)[] options =
        [
            ("data access", Flag(server.DataAccess)), ("rpc", Flag(server.Rpc)), ("rpc out", Flag(server.RpcOut)),
            ("collation compatible", Flag(server.CollationCompatible)), ("use remote collation", Flag(server.UseRemoteCollation)),
            ("lazy schema validation", Flag(server.LazySchemaValidation)),
            ("remote proc transaction promotion", Flag(server.RemoteProcTransactionPromotion)),
            ("connect timeout", server.ConnectTimeout.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            ("query timeout", server.QueryTimeout.ToString(System.Globalization.CultureInfo.InvariantCulture)),
        ];
        foreach ((string name, string value) in options)
        {
            lines.Add($"EXEC sp_serveroption @server = {SqlText.Literal(server.LinkedServerName)}, @optname = {SqlText.Literal(name)}, @optvalue = {SqlText.Literal(value)};");
        }
        lines.Add($"EXEC sp_serveroption @server = {SqlText.Literal(server.LinkedServerName)}, @optname = N'collation name', @optvalue = {SqlText.Literal(server.CollationName)};");
        return string.Join('\n', lines);
    }

    private static string Flag(bool value) => value ? "true" : "false";

    public static string Build(
        string name,
        string? product,
        string? provider,
        string? dataSource,
        string? providerString,
        string? catalog,
        IReadOnlyList<(string? RemoteLoginName, bool? UsesSelfCredential)> logins,
        string? location = null)
    {
        List<string> lines =
        [
            "EXEC sp_addlinkedserver",
            $"    @server = N'{Quote(name)}',",
            $"    @srvproduct = N'{Quote(product)}',",
            $"    @provider = N'{Quote(provider)}',",
            $"    @datasrc = N'{Quote(dataSource)}',",
            $"    @location = {SqlText.Literal(location)},",
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
