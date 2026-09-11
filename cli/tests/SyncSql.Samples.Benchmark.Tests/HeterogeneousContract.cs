using System.Text.Json;
using SyncSql.Core.Domain;
using SyncSql.Core.Json;

namespace SyncSql.Samples.Benchmark.Tests;

// This contract contains no SQL, connection strings, parser results or recorded baselines.
internal sealed record HeterogeneousContract(
    List<ScenarioScope> Scopes,
    List<ScenarioUser> Users,
    List<ScenarioNode> Nodes,
    List<ScenarioDependency> Dependencies,
    List<ScenarioGrant> Grants,
    List<ScenarioPath> Paths)
{
    public static string Root => Path.Combine(SampleFleet.SamplesRoot, "scenarios", "heterogeneous-lineage");
    public static HeterogeneousContract Load() =>
        JsonSerializer.Deserialize<HeterogeneousContract>(
            File.ReadAllText(Path.Combine(Root, "expected-catalog.json")), SyncSqlJsonOptions.Default)!;

    public void AssertCatalog(Core.Domain.Catalog actual)
    {
        Assert.Equal(Nodes.Select(n => n.Id).Order(StringComparer.Ordinal),
            actual.Nodes.Select(n => n.Id).Order(StringComparer.Ordinal));
        Dictionary<string, CatalogNode> index = actual.Nodes.ToDictionary(n => n.Id);
        foreach (ScenarioNode expected in Nodes)
        {
            CatalogNode node = index[expected.Id];
            Assert.Equal(expected.Type, node.Type);
            Assert.Equal(expected.Schema, node.Schema);
            Assert.Equal(expected.Columns.Order(StringComparer.Ordinal),
                node.Columns.Select(c => c.Name).Order(StringComparer.Ordinal));
        }

        // Compare the complete edge set, including the link nodes rendered by the catalog.
        // A link is shared by many callers: paths below use the attributed references to
        // avoid inventing a path through somebody else's target on that same link.
        HashSet<string> expectedEdges = new(StringComparer.Ordinal);
        foreach (ScenarioDependency dependency in Dependencies)
        {
            if (dependency.Via is { } link)
            {
                expectedEdges.Add(dependency.From + "|" + link);
                expectedEdges.Add(link + "|" + dependency.To);
            }
            else
            {
                expectedEdges.Add(dependency.From + "|" + dependency.To);
            }
        }
        HashSet<string> actualEdges = actual.Edges.Select(e => e.From + "|" + e.To).ToHashSet();
        Assert.True(expectedEdges.SetEquals(actualEdges),
            "Missing edges:\n" + string.Join("\n", expectedEdges.Except(actualEdges))
            + "\nUnexpected edges:\n" + string.Join("\n", actualEdges.Except(expectedEdges)));
        Assert.Equal(Dependencies.Where(d => d.Via is not null).Select(d => $"{d.From}|{d.Via}|{d.To}|{d.Dynamic}").Order(StringComparer.Ordinal),
            actual.LinkedServerReferences.Select(r => $"{r.From}|{r.LinkedServer}|{r.To}|{r.Dynamic}").Order(StringComparer.Ordinal));
        Assert.Empty(actual.OrphanedReferences);

        HashSet<string> workloadUsers = Users.Select(u => u.Name).ToHashSet(StringComparer.Ordinal);
        Assert.Equal(Grants.Select(GrantKey).Order(StringComparer.Ordinal),
            actual.Nodes.SelectMany(n => n.Grants.Where(g => workloadUsers.Contains(g.Grantee))
                .Select(g => GrantKey(new ScenarioGrant(n.Id, g.Grantee, g.Permission,
                    g.State == GrantState.Grant ? "GRANT" : "DENY", g.Column)))).Order(StringComparer.Ordinal));
        foreach (ScenarioUser user in Users)
        {
            // Explicit privileges are not an assertion about remote impersonation, role
            // inheritance, ownership chains or effective authorization at execution time.
            Assert.Equal(Grants.Where(g => g.User == user.Name).Select(g => g.Object).Distinct().Order(StringComparer.Ordinal),
                actual.Nodes.Where(n => n.Grants.Any(g => g.Grantee == user.Name))
                    .Select(n => n.Id).Order(StringComparer.Ordinal));
        }

        HashSet<string> links = Nodes.Where(n => n.Type is "LinkedServers" or "DatabaseLinks").Select(n => n.Id).ToHashSet();
        HashSet<string> semanticEdges = actual.Edges.Where(e => !links.Contains(e.From) && !links.Contains(e.To))
            .Select(e => e.From + "|" + e.To).Concat(actual.LinkedServerReferences.Select(r => r.From + "|" + r.To)).ToHashSet();
        string[] expectedSemanticEdges = [.. Dependencies.Select(d => d.From + "|" + d.To)];
        foreach (ScenarioUser user in Users)
        {
            string[] expectedRoots = [.. Grants.Where(g => g.User == user.Name && g.State == "GRANT").Select(g => g.Object)];
            string[] actualRoots = [.. actual.Nodes.Where(n => n.Grants.Any(g => g.Grantee == user.Name && g.State == GrantState.Grant)).Select(n => n.Id)];
            Assert.Equal(Reachable(expectedSemanticEdges, expectedRoots).Order(StringComparer.Ordinal),
                Reachable(semanticEdges, actualRoots).Order(StringComparer.Ordinal));
        }
        // Traversal by any table, view, procedure, trigger or package must terminate
        // even when its dependency graph contains a cycle.
        foreach (ScenarioNode node in Nodes.Where(n => !links.Contains(n.Id)))
        {
            Assert.Equal(Reachable(expectedSemanticEdges, [node.Id]).Order(StringComparer.Ordinal),
                Reachable(semanticEdges, [node.Id]).Order(StringComparer.Ordinal));
        }

        foreach (ScenarioPath path in Paths)
        {
            for (int i = 1; i < path.Nodes.Count; i++)
            {
                Assert.True(semanticEdges.Contains(path.Nodes[i - 1] + "|" + path.Nodes[i]),
                    $"Missing hop {i} in {path.Name}: {path.Nodes[i - 1]} -> {path.Nodes[i]}");
            }
        }
    }

    private static string GrantKey(ScenarioGrant g) => $"{g.Object}|{g.User}|{g.Permission}|{g.State}|{g.Column}";

    internal static HashSet<string> Reachable(IEnumerable<string> edges, IEnumerable<string> roots)
    {
        ILookup<string, string> adjacency = edges.Select(e => e.Split('|')).ToLookup(e => e[0], e => e[1]);
        HashSet<string> visited = new(StringComparer.Ordinal);
        Queue<string> pending = new(roots);
        while (pending.TryDequeue(out string? current))
        {
            if (!visited.Add(current))
            {
                continue;
            }

            foreach (string next in adjacency[current])
            {
                pending.Enqueue(next);
            }
        }
        return visited;
    }
}

internal sealed record ScenarioScope(string Key, string Server, string Database, string Schema, string Engine, string? Package);
internal sealed record ScenarioUser(string Name, string Scope, string Server, string Database);
internal sealed record ScenarioNode(string Id, string Scope, string Server, string Database, string? Schema, string Type, string Name, List<string> Columns);
internal sealed record ScenarioDependency(string From, string To, string? Via, bool Dynamic);
internal sealed record ScenarioGrant(string Object, string User, string Permission, string State, string? Column);
internal sealed record ScenarioPath(string Name, List<string> Nodes);
