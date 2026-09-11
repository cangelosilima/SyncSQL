using System.Data.Common;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Data.SqlClient;
using Oracle.ManagedDataAccess.Client;
using SyncSql.Core.Configuration;
using SyncSql.Core.Abstractions;
using SyncSql.Core.Domain;
using SyncSql.Core.Json;
using SyncSql.Core.Serialization;
using SyncSql.Extraction.Oracle;
using Xunit.Abstractions;

namespace SyncSql.Samples.Benchmark.Tests;

public sealed class HeterogeneousFleetFactAttribute : FactAttribute
{
    public HeterogeneousFleetFactAttribute()
    {
        if (!HeterogeneousFleet.Enabled)
        {
            Skip = "Run samples/scenarios/heterogeneous-lineage/run.ps1 (or run.sh) to provision and benchmark the Docker fleet.";
        }
    }
}

[Collection("heterogeneous-live")]
public sealed class HeterogeneousLiveTests(ITestOutputHelper output)
{
    [HeterogeneousFleetFact]
    public async Task Provision_extract_and_compare_full_catalog_and_user_access()
    {
        string runRoot = Path.Combine(HeterogeneousContract.Root, ".cache", "runs", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(runRoot);
        string objectsRoot = Path.Combine(runRoot, "extracted");
        string credentialsPath = Path.Combine(runRoot, "credentials.json");
        Dictionary<string, object> credentials = HeterogeneousFleet.Config.Servers.ToDictionary(
            s => s.CredentialsVariablePrefix,
            s => (object)new { user = s.Type == DatabaseEngine.MsSql ? "sa" : "SYSTEM", password = HeterogeneousFleet.Password });
        await File.WriteAllTextAsync(credentialsPath, JsonSerializer.Serialize(credentials));
        SyncSqlPipeline pipeline = new();
        Dictionary<string, double> timings = [];
        bool passed = false;
        bool gatewayPassed = false;
        using CancellationTokenSource timeout = new(TimeSpan.FromMinutes(20));
        Stopwatch watch = Stopwatch.StartNew();
        try
        {
            await new HeterogeneousFleet().ProvisionAsync(timeout.Token);
            timings["provisionSeconds"] = watch.Elapsed.TotalSeconds;
            watch.Restart();
            await AssertUserAccessAsync(timeout.Token);
            await AssertOwnerExtractionAsync(timeout.Token);
            await HeterogeneousGateway.AssertBridgePermissionsAsync(timeout.Token);
            timings["userAccessSeconds"] = watch.Elapsed.TotalSeconds;
            watch.Restart();
            if (HeterogeneousFleet.GatewayEnabled)
            {
                await HeterogeneousGateway.AssertExecutionAsync(timeout.Token);
                gatewayPassed = true;
                timings["gatewayExecutionSeconds"] = watch.Elapsed.TotalSeconds;
                watch.Restart();
            }
            await pipeline.RunScenarioAsync(HeterogeneousFleet.ConfigPath, objectsRoot, credentialsPath, timeout.Token);
            timings["extractAndCatalogSeconds"] = watch.Elapsed.TotalSeconds;
            watch.Restart();
            Core.Domain.Catalog catalog = JsonSerializer.Deserialize<Core.Domain.Catalog>(
                await File.ReadAllTextAsync(Path.Combine(objectsRoot, "catalog.json"), timeout.Token), SyncSqlJsonOptions.Default)!;
            HeterogeneousContract.Load().AssertCatalog(catalog);
            Assert.All(catalog.Nodes.Where(n => n.Type == "LinkedServers" && n.Name == "HELIOS_ORACLE"), link =>
            {
                Assert.Contains("Oracle (lineage metadata only)", link.Ddl, StringComparison.Ordinal);
                Assert.Contains("MSOLEDBSQL", link.Ddl, StringComparison.Ordinal);
            });
            foreach (CatalogNode node in catalog.Nodes)
            {
                string path = Path.Combine(objectsRoot, node.Path);
                Assert.True(File.Exists(path), $"Missing extracted file: {node.Id}");
                ParsedObjectFile parsed = ExtractedObjectFile.Parse(await File.ReadAllLinesAsync(path, timeout.Token));
                Assert.NotNull(parsed.Identity);
                Assert.Equal(node.Server, parsed.Identity.Server);
            }
            // Packages are catalog objects; each body must preserve all ten callable members.
            foreach (CatalogNode package in catalog.Nodes.Where(n => n.Type == "PackageBodies"))
            {
                for (int member = 1; member <= 10; member++)
                {
                    Assert.Contains($"F{member:00}", package.Ddl, StringComparison.OrdinalIgnoreCase);
                }
            }

            await WriteLineageIndexAsync(catalog, runRoot);
            Assert.True(Directory.EnumerateFiles(Path.Combine(objectsRoot, "metrics"), "*.json", SearchOption.AllDirectories).Any());
            timings["assertSeconds"] = watch.Elapsed.TotalSeconds;
            passed = true;
        }
        finally
        {
            File.Delete(credentialsPath);
            await File.WriteAllTextAsync(Path.Combine(runRoot, "pipeline.log"), pipeline.Log);
            await File.WriteAllTextAsync(Path.Combine(runRoot, "benchmark.json"), JsonSerializer.Serialize(new
            {
                scenario = "heterogeneous-lineage",
                passed,
                timings,
                oracleLinkedServerRegistration = "metadata-only: labelled MSOLEDBSQL transport placeholders; Linux rejects Oracle OLE DB registration",
                oracleToSqlServerExecution = gatewayPassed ? "passed: dg4msql, four databases, both schemas, workload permissions and SQL linked-server hop"
                    : HeterogeneousFleet.GatewayEnabled ? "failed or not reached" : "not requested: run with -Gateway / --gateway",
                sqlToOracleExecution = "not supported by Linux SQL Server Oracle linked-server placeholders",
                gatewayRequested = HeterogeneousFleet.GatewayEnabled,
                runtime = Environment.Version.ToString(),
            }, SyncSqlJsonOptions.Indented));
            output.WriteLine($"Benchmark artifacts: {runRoot}");
        }
    }

    private static async Task AssertUserAccessAsync(CancellationToken token)
    {
        HeterogeneousContract contract = HeterogeneousContract.Load();
        Assert.Equal(contract.Users.Select(u => u.Name).Order(StringComparer.Ordinal),
            HeterogeneousFleet.Principals.Select(p => p.Name).Order(StringComparer.Ordinal));
        foreach (ScenarioUser user in contract.Users)
        {
            ServerConfig server = HeterogeneousFleet.Config.Servers.Single(s => s.Name == user.Server);
            ScenarioScope scope = contract.Scopes.Single(s => s.Key == user.Scope);
            await using DbConnection connection = await HeterogeneousFleet.OpenAsync(server, user.Database, user.Name, token);
            bool reader = user.Name.EndsWith("_READ", StringComparison.Ordinal);
            if (server.Type == DatabaseEngine.MsSql)
            {
                int allowed = Convert.ToInt32(await HeterogeneousFleet.ScalarAsync(connection,
                    $"SELECT HAS_PERMS_BY_NAME('{scope.Schema}.ITEMS', 'OBJECT', 'SELECT')", token), CultureInfo.InvariantCulture);
                Assert.Equal(reader ? 1 : 0, allowed);
                int canExecute = Convert.ToInt32(await HeterogeneousFleet.ScalarAsync(connection,
                    $"SELECT HAS_PERMS_BY_NAME('{scope.Schema}.P_READ', 'OBJECT', 'EXECUTE')", token), CultureInfo.InvariantCulture);
                Assert.Equal(user.Name.EndsWith("_EXEC", StringComparison.Ordinal) ? 1 : 0, canExecute);
            }
            else if (reader)
            {
                await HeterogeneousFleet.ScalarAsync(connection, $"SELECT COUNT(*) FROM {scope.Schema}.ITEMS", token);
            }
            else
            {
                OracleException error = await Assert.ThrowsAsync<OracleException>(() =>
                    HeterogeneousFleet.ScalarAsync(connection, $"SELECT COUNT(*) FROM {scope.Schema}.ITEMS", token));
                // Oracle 23 also reports missing object privileges as ORA-41900.
                Assert.Contains(error.Number, new[] { 942, 1031, 41900 });
            }
        }
    }

    private static Task WriteLineageIndexAsync(Core.Domain.Catalog catalog, string runRoot)
    {
        HashSet<string> linkIds = catalog.Nodes.Where(n => n.Type is "LinkedServers" or "DatabaseLinks").Select(n => n.Id).ToHashSet();
        string[] edges = [.. catalog.Edges.Where(e => !linkIds.Contains(e.From) && !linkIds.Contains(e.To))
            .Select(e => e.From + "|" + e.To).Concat(catalog.LinkedServerReferences.Select(r => r.From + "|" + r.To))];
        return File.WriteAllTextAsync(Path.Combine(runRoot, "lineage-index.json"), JsonSerializer.Serialize(new
        {
            byUser = HeterogeneousFleet.Principals.ToDictionary(u => u.Name, u => new
            {
                directlyGrantedObjects = catalog.Nodes.Where(n => n.Grants.Any(g => g.Grantee == u.Name && g.State == GrantState.Grant)).Select(n => n.Id).ToArray(),
                dependencyObjects = HeterogeneousContract.Reachable(edges,
                    catalog.Nodes.Where(n => n.Grants.Any(g => g.Grantee == u.Name && g.State == GrantState.Grant)).Select(n => n.Id)).Order(StringComparer.Ordinal).ToArray(),
            }),
            byType = catalog.Nodes.GroupBy(n => n.Type).ToDictionary(g => g.Key, g => g.Select(n => n.Id).Order(StringComparer.Ordinal).ToArray()),
            paths = HeterogeneousContract.Load().Paths,
        }, SyncSqlJsonOptions.Indented));
    }

    private static async Task AssertOwnerExtractionAsync(CancellationToken token)
    {
        // Exercises the fallback from DBA_* to ALL_* with a schema owner who has
        // no catalog role. A fix for SYSTEM must not break ordinary extraction.
        ServerConfig server = HeterogeneousFleet.Config.Servers.Single(s => s.Type == DatabaseEngine.Oracle) with
        {
            Schemas = new NameFilter { Include = ["^PROCUREMENT$"] },
            ObjectTypes = ["Tables", "DatabaseLinks"],
        };
        ExtractionOutcome result = await new OracleObjectExtractor(NullLogger<OracleObjectExtractor>.Instance, TimeProvider.System)
            .ExtractAsync(server, EffectiveFilters.Resolve(null, server),
                new ExtractionOptions { Credentials = new DatabaseCredentials("PROCUREMENT", HeterogeneousFleet.Password), CaptureMetrics = false }, token);
        Assert.Equal(7, result.Objects.Count);
        Assert.Equal(3, result.Objects.Count(o => o.Type == "DatabaseLinks"));
        ExtractedObject items = Assert.Single(result.Objects, o => o.Name == "ITEMS");
        Assert.Contains(items.Grants, g => g.Grantee == "PROCUREMENT_READ" && g.Permission == "SELECT");
        Assert.Contains(items.Grants, g => g.Grantee == "PROCUREMENT_WRITE" && g.Column == "AMOUNT");
    }
}

[CollectionDefinition("heterogeneous-live", DisableParallelization = true)]
public sealed class HeterogeneousLiveCollection;
