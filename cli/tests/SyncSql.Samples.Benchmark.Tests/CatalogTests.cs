using SyncSql.Core.Domain;
using SyncSql.Core.Serialization;

namespace SyncSql.Samples.Benchmark.Tests;

/// <summary>
/// The catalog half of the benchmark: what `syncsql catalog build` inferred from the extracted tree.
/// The lineage assertions are the point of the exercise - every edge here was read out of a view or
/// module body in the upstream samples, so a regression in the T-SQL or PL/SQL analyzers shows up as
/// a specific missing edge rather than as a number that moved.
/// </summary>
[Collection(SampleBenchmarkCollection.Name)]
public sealed class CatalogTests(SampleBenchmarkFixture fleet)
{
    [SampleFleetFact]
    public void CatalogCoversEverySampleServer()
    {
        foreach (string server in fleet.Expectations.Servers)
        {
            Assert.Contains(server, fleet.Catalog.Servers);
        }
    }

    [SampleFleetFact]
    public void EveryExpectedObjectIsACatalogNode()
    {
        Dictionary<string, CatalogNode> byId = NodesById();
        List<string> missing = [];

        foreach (ExpectedGroup group in fleet.Expectations.Groups)
        {
            foreach (ExpectedObject expected in group.Objects)
            {
                string id = ExtractedObjectFile.ObjectId(
                    group.Server, group.Database, expected.ResolveSchema(group), expected.Type, expected.Name);
                if (!byId.ContainsKey(id))
                {
                    missing.Add($"{group.Describe()}: {id}");
                }
            }
        }

        Assert.True(missing.Count == 0, BenchmarkReport.Describe("Expected objects with no catalog node", missing));
    }

    [SampleFleetFact]
    public void EveryExpectedLineageEdgeWasInferred()
    {
        HashSet<string> edges = new(
            fleet.Catalog.Edges.Select(edge => $"{edge.From} -> {edge.To}"),
            StringComparer.OrdinalIgnoreCase);

        List<string> missing = [];
        foreach (ExpectedEdge expected in fleet.Expectations.Edges)
        {
            if (!edges.Contains($"{expected.From} -> {expected.To}"))
            {
                missing.Add($"{expected.SampleId}: {expected.From} -> {expected.To}");
            }
        }

        Assert.True(missing.Count == 0, BenchmarkReport.Describe("Lineage edges the catalog did not infer", missing));
    }

    [SampleFleetFact]
    public void EachDatabaseMeetsItsMinimumSize()
    {
        List<string> undersized = [];

        foreach (ExpectedGroup group in fleet.Expectations.Groups.Where(g => g.MinimumNodeCount is not null))
        {
            int actual = fleet.NodesFor(group).Count;
            if (actual < group.MinimumNodeCount!.Value)
            {
                undersized.Add($"{group.Describe()}: expected at least {group.MinimumNodeCount}, found {actual}");
            }
        }

        Assert.True(undersized.Count == 0, BenchmarkReport.Describe("Databases with fewer catalogued objects than expected", undersized));
    }

    [SampleFleetFact]
    public void TypeCountsAgreeWithTheNodesTheySummarize()
    {
        Dictionary<string, int> fromNodes = fleet.Catalog.Nodes
            .GroupBy(node => node.Type, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

        Assert.Equal(
            fromNodes.OrderBy(pair => pair.Key, StringComparer.Ordinal),
            fleet.Catalog.TypeCounts.OrderBy(pair => pair.Key, StringComparer.Ordinal));
    }

    [SampleFleetFact]
    public void EveryNodeCarriesItsEngineAndSomeDdl()
    {
        List<string> problems = [];

        foreach (CatalogNode node in fleet.Catalog.Nodes)
        {
            if (node.Engine is null)
            {
                problems.Add($"{node.Id}: no engine recorded");
            }
            if (string.IsNullOrWhiteSpace(node.Ddl))
            {
                problems.Add($"{node.Id}: empty DDL");
            }
        }

        Assert.True(problems.Count == 0, BenchmarkReport.Describe("Catalog nodes missing engine or DDL", problems));
    }

    [SampleFleetFact]
    public void EveryEdgeConnectsTwoNodesThatExist()
    {
        Dictionary<string, CatalogNode> byId = NodesById();

        List<string> dangling = [];
        foreach (CatalogEdge edge in fleet.Catalog.Edges)
        {
            if (!byId.ContainsKey(edge.From))
            {
                dangling.Add($"edge from unknown node '{edge.From}'");
            }
            if (!byId.ContainsKey(edge.To))
            {
                dangling.Add($"edge to unknown node '{edge.To}'");
            }
        }

        Assert.True(dangling.Count == 0, BenchmarkReport.Describe("Edges pointing at nodes that are not in the catalog", dangling));
    }

    /// <summary>
    /// An orphaned reference means "this DDL names something nobody extracted". When the reference is
    /// fully qualified and the thing it names *is* catalogued, the resolver got it wrong - which is the
    /// failure mode this fleet exists to catch, spanning as it does two engines, ten databases and
    /// every object type SyncSQL knows.
    ///
    /// Only explicitly schema-qualified references are checked. An unqualified name resolves through
    /// the engine's own default-schema rules, so guessing at its schema here would invent failures
    /// rather than find them.
    /// </summary>
    [SampleFleetFact]
    public void NoQualifiedOrphanedReferencePointsAtSomethingTheCatalogHas()
    {
        Dictionary<string, CatalogNode> byId = NodesById();
        HashSet<string> qualified = new(
            fleet.Catalog.Nodes.Select(node => $"{node.Server}/{node.Database}/{node.Schema}/{node.Name}"),
            StringComparer.OrdinalIgnoreCase);

        List<string> wrong = [];
        foreach (CatalogOrphanedReference reference in fleet.Catalog.OrphanedReferences)
        {
            if (reference.Server is not null || reference.Schema is null)
            {
                continue;
            }
            if (!byId.TryGetValue(reference.From, out CatalogNode? source))
            {
                continue;
            }

            string database = reference.Database ?? source.Database;
            if (qualified.Contains($"{source.Server}/{database}/{reference.Schema}/{reference.Name}"))
            {
                wrong.Add($"{reference.From} -> {database}.{reference.Schema}.{reference.Name} is catalogued but reported as orphaned");
            }
        }

        Assert.True(wrong.Count == 0, BenchmarkReport.Describe("Orphaned references whose target is in the catalog", wrong));
    }

    private Dictionary<string, CatalogNode> NodesById() =>
        fleet.Catalog.Nodes.ToDictionary(node => node.Id, StringComparer.OrdinalIgnoreCase);
}
