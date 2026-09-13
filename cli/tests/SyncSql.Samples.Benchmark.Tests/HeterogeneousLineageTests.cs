using Microsoft.Extensions.Logging.Abstractions;
using SyncSql.Catalog;
using SyncSql.Core.Abstractions;
using SyncSql.Core.Domain;
using SyncSql.Core.Serialization;
using SyncSql.Lineage.MsSql;
using SyncSql.Lineage.Oracle;
using System.Text.RegularExpressions;

namespace SyncSql.Samples.Benchmark.Tests;

public sealed class HeterogeneousLineageTests
{
    [Fact]
    public void Contract_covers_publication_sources_and_four_local_Broker_flows()
    {
        HeterogeneousContract contract = HeterogeneousContract.Load();
        Assert.Equal(2, contract.Nodes.Count(n => n.Type == "Replication"));
        foreach (ScenarioNode publication in contract.Nodes.Where(n => n.Type == "Replication"))
        {
            ScenarioDependency[] articles = [.. contract.Dependencies.Where(d => d.From == publication.Id)];
            Assert.Equal(2, articles.Length);
            Assert.All(articles, article => Assert.Contains(contract.Nodes,
                n => n.Id == article.To && n.Type == "Tables" && n.Name == "ITEMS" && n.Database == publication.Database));
        }
        foreach (var database in contract.Nodes.Where(n => n.Type == "Services").GroupBy(n => (n.Server, n.Database)))
        {
            Assert.Equal(2, database.Count());
            Assert.Equal(2, contract.Nodes.Count(n => n.Type == "Queues" && (n.Server, n.Database) == database.Key));
            Assert.Single(contract.Nodes, n => n.Type == "Contracts" && (n.Server, n.Database) == database.Key);
            Assert.Single(contract.Nodes, n => n.Type == "MessageTypes" && (n.Server, n.Database) == database.Key);
        }
        Assert.Equal(4, contract.Nodes.Where(n => n.Type == "Services").Select(n => (n.Server, n.Database)).Distinct().Count());
    }

    [Fact]
    public void Gateway_routes_every_private_link_to_its_expected_SQL_database()
    {
        string root = Path.Combine(HeterogeneousContract.Root, "gateway");
        string tns = File.ReadAllText(Path.Combine(root, "oracle", "tnsnames.ora"));
        string listener = File.ReadAllText(Path.Combine(root, "network", "listener.ora"));
        HeterogeneousContract contract = HeterogeneousContract.Load();
        HashSet<string> databases = [];
        foreach (ExtractedObject link in HeterogeneousDdl.LoadObjects().Where(o => o.Type == "DatabaseLinks"))
        {
            string alias = Regex.Match(link.Ddl, @"USING '([^']+)'").Groups[1].Value;
            string entry = Assert.Single(tns.Split('\n'), line => line.StartsWith(alias + "=", StringComparison.Ordinal));
            Assert.Contains("(HOST=HERMES_GATEWAY)", entry, StringComparison.Ordinal);
            Assert.Contains("(HS=OK)", entry, StringComparison.Ordinal);
            string sid = Regex.Match(entry, @"\(SID=([^)]*)\)").Groups[1].Value;
            Assert.Contains($"(SID_NAME={sid})", listener, StringComparison.Ordinal);
            string init = File.ReadAllText(Path.Combine(root, "network", $"init{sid}.ora"));
            Assert.Contains("HS_TRANSACTION_MODEL=READ_ONLY", init, StringComparison.Ordinal);
            string id = $"{link.Server}/{link.Database}/DatabaseLinks/{link.Schema}/{link.Name}";
            foreach (ScenarioDependency dependency in contract.Dependencies.Where(d => d.Via == id))
            {
                ScenarioNode target = contract.Nodes.Single(n => n.Id == dependency.To);
                Assert.Contains($"HS_FDS_CONNECT_INFO={target.Server}:1433//{target.Database}\n", init, StringComparison.Ordinal);
                databases.Add(target.Database);
            }
        }
        Assert.Equal(new[] { "Commerce", "Distribution", "Intelligence", "Receivables" }, databases.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Contract_covers_all_scopes_users_and_required_paths()
    {
        HeterogeneousContract contract = HeterogeneousContract.Load();
        Assert.Equal(16, contract.Scopes.Count);
        Assert.Equal(48, contract.Users.Select(u => u.Name).Distinct().Count());
        Assert.Equal(3, contract.Scopes.Select(s => s.Server).Distinct().Count());
        Assert.Equal(4, contract.Scopes.Where(s => s.Engine == "mssql").Select(s => s.Database).Distinct().Count());
        Assert.Equal(2, contract.Scopes.Count(s => s.Package is not null));
        Assert.Equal(14, contract.Scopes.Where(s => s.Package is null).Select(s => s.Schema).Distinct().Count());
        Assert.DoesNotContain(contract.Scopes, s => s.Schema == s.Database);
        foreach (ScenarioScope scope in contract.Scopes)
        {
            Assert.Equal(3, contract.Users.Count(u => u.Scope == scope.Key));
            if (scope.Package is null)
            {
                Assert.True(contract.Nodes.Count(n => n.Scope == scope.Key) >= 10);
            }
        }
        Assert.Equal(16, contract.Paths.Count);
        Assert.Equal(2, contract.Paths.Count(p => p.Nodes[0] == p.Nodes[^1]));
        Assert.All(contract.Dependencies, d =>
        {
            Assert.Contains(contract.Nodes, n => n.Id == d.From);
            Assert.Contains(contract.Nodes, n => n.Id == d.To);
            if (d.Via is not null)
            {
                Assert.Contains(contract.Nodes, n => n.Id == d.Via);
            }
        });
    }

    [Fact]
    public async Task Catalog_from_authored_DDL_matches_independent_contract()
    {
        string root = Directory.CreateTempSubdirectory("syncsql-heterogeneous-").FullName;
        try
        {
            var config = await Core.Configuration.SyncSqlConfigLoader.LoadAsync(Path.Combine(HeterogeneousContract.Root, "servers.json"));
            foreach (ExtractedObject obj in HeterogeneousDdl.LoadObjects())
            {
                string path = Path.Combine(root, ExtractedObjectFile.RelativePath(obj.Server, obj.Database, obj.Schema, obj.Type, obj.Name));
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                var identity = Core.Configuration.ServerIdentity.FromConfig(config.Servers.Single(server => server.Name == obj.Server));
                await File.WriteAllTextAsync(path, ExtractedObjectFile.Write(obj with { ServerIdentity = identity }));
            }
            Core.Domain.Catalog catalog = await BuildCatalogAsync(root);
            HeterogeneousContract.Load().AssertCatalog(catalog);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    internal static Task<Core.Domain.Catalog> BuildCatalogAsync(string root) => new CatalogBuilder(
        new ScenarioAnalyzers(),
        new UnusedHistory(),
        new MetricsHistoryStore(NullLogger<MetricsHistoryStore>.Instance),
        TimeProvider.System,
        NullLogger<CatalogBuilder>.Instance)
        .BuildAsync(new CatalogBuildRequest { ObjectsRoot = root }, CancellationToken.None);

    private sealed class ScenarioAnalyzers : ILineageAnalyzerResolver
    {
        public ILineageAnalyzer Resolve(DatabaseEngine engine) => engine == DatabaseEngine.Oracle
            ? new OracleLineageAnalyzer(NullLogger<OracleLineageAnalyzer>.Instance)
            : new MsSqlLineageAnalyzer(NullLogger<MsSqlLineageAnalyzer>.Instance);
    }

    private sealed class UnusedHistory : IGitHistoryMiner
    {
        public Task<GitHistoryMiningResult> MineAsync(GitHistoryMiningRequest request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("This deterministic benchmark must not mine repository history.");
    }
}
