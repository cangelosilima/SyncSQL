using Antlr4.Runtime;
using Microsoft.Extensions.Logging.Abstractions;
using System.Runtime.CompilerServices;
using Antlr4.Runtime.Atn;
using Antlr4.Runtime.Dfa;
using SyncSql.Core.Abstractions;

namespace SyncSql.Lineage.Oracle.Tests;

public sealed class ParserMemoryTests
{
    private sealed class DistinctState() : DFAState(new ATNConfigSet())
    {
        public override bool Equals(object? obj) => ReferenceEquals(this, obj);
        public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Analyze_DiscardsOversizedPredictionTablesBeforeNextObject(bool manyStates)
    {
        WeakReference? expired = null;
        var analyzer = new OracleLineageAnalyzer(NullLogger<OracleLineageAnalyzer>.Instance, parser =>
        {
            var tree = parser.sql_script();
            if (expired is null)
            {
                expired = new WeakReference(parser.Interpreter.decisionToDFA);
                DFA decision = new(new BasicBlockStartState(), 0);
                if (manyStates)
                {
                    for (int i = 0; i <= OracleLineageAnalyzer.MaxCachedStates; i++)
                    {
                        var state = new DistinctState();
                        decision.states.Add(state, state);
                    }
                }
                else
                {
                    var state = new DistinctState();
                    // Inflate the external runtime table after parsing; only its retention
                    // policy is under test, and the discarded table must never parse again.
                    for (int i = 0; i <= OracleLineageAnalyzer.MaxCachedConfigurations; i++)
                    {
                        state.configSet.configs.Add(null!);
                    }
                    decision.states.Add(state, state);
                }
                parser.Interpreter.decisionToDFA[0] = decision;
            }
            else
            {
                Assert.NotSame(expired.Target, parser.Interpreter.decisionToDFA);
            }
            return tree;
        });
        analyzer.Analyze("SELECT o.id FROM app.orders o;");
        var result = analyzer.Analyze("SELECT c.id FROM app.customers c;");
        Assert.Contains(result.ObjectRefs, reference => reference.Name == "customers");
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Assert.False(expired!.IsAlive);
        GC.KeepAlive(analyzer);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Analyze_PredictionCachesAreCollectibleAfterBoundedReuseOrFailure(bool failParsing)
    {
        List<WeakReference> caches = [];
        var analyzer = new OracleLineageAnalyzer(NullLogger<OracleLineageAnalyzer>.Instance, parser =>
        {
            caches.Add(new WeakReference(parser.Interpreter.decisionToDFA));
            var lexer = (PlSqlLexer)((CommonTokenStream)parser.TokenStream).TokenSource;
            caches.Add(new WeakReference(lexer.Interpreter.decisionToDFA));
            if (failParsing)
            {
                throw new InvalidOperationException("Simulated parse failure");
            }
            return parser.sql_script();
        });

        for (int i = 0; i < OracleLineageAnalyzer.MaxCachedScripts; i++)
        {
            var result = analyzer.Analyze("SELECT o.id FROM app.orders o;", new LineageAnalysisOptions { DynamicSql = false });
            if (!failParsing)
            {
                Assert.Contains(result.ObjectRefs, reference => reference.Name == "orders");
                Assert.Contains(result.ColumnRefs, reference => reference.Column == "id");
            }
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Assert.Equal(OracleLineageAnalyzer.MaxCachedScripts * 2, caches.Count);
        Assert.All(caches, cache => Assert.False(cache.IsAlive));
        GC.KeepAlive(analyzer);
    }

    [Fact]
    public void Analyze_ReusesSmallCacheButDiscardsItAfterLargeInput()
    {
        List<WeakReference> caches = [];
        var analyzer = new OracleLineageAnalyzer(NullLogger<OracleLineageAnalyzer>.Instance, parser =>
        {
            caches.Add(new WeakReference(parser.Interpreter.decisionToDFA));
            return parser.sql_script();
        });
        const string sql = "SELECT o.id FROM app.orders o;";
        analyzer.Analyze(sql);
        analyzer.Analyze(sql);
        AssertReused(caches[0], caches[1]);
        analyzer.Analyze(sql + " --" + new string('x', OracleLineageAnalyzer.MaxCachedCharacters));
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Assert.All(caches, cache => Assert.False(cache.IsAlive));
        GC.KeepAlive(analyzer);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void AssertReused(WeakReference first, WeakReference second) => Assert.Same(first.Target, second.Target);

    [Fact]
    public void Analyze_WarmCacheDoesNotRetainParserOrInput()
    {
        List<WeakReference> objects = [];
        var analyzer = new OracleLineageAnalyzer(NullLogger<OracleLineageAnalyzer>.Instance, parser =>
        {
            objects.Add(new WeakReference(parser));
            objects.Add(new WeakReference(parser.TokenStream));
            objects.Add(new WeakReference(((CommonTokenStream)parser.TokenStream).TokenSource));
            return parser.sql_script();
        });
        analyzer.Analyze("SELECT o.id FROM app.orders o;");
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Assert.All(objects, value => Assert.False(value.IsAlive));
        GC.KeepAlive(analyzer);
    }

    [Fact]
    public async Task Analyze_ConcurrentCallsUseIndependentCaches()
    {
        using CountdownEvent entered = new(2);
        using ManualResetEventSlim release = new();
        var caches = new System.Collections.Concurrent.ConcurrentBag<object>();
        var analyzer = new OracleLineageAnalyzer(NullLogger<OracleLineageAnalyzer>.Instance, parser =>
        {
            caches.Add(parser.Interpreter.decisionToDFA);
            entered.Signal();
            if (!release.Wait(TimeSpan.FromSeconds(10)))
            {
                throw new TimeoutException();
            }
            return parser.sql_script();
        });
        var calls = Enumerable.Range(0, 2).Select(i => Task.Factory.StartNew(
            () => analyzer.Analyze($"SELECT o.id FROM app.orders{i} o;"),
            CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default)).ToArray();
        bool overlapped;
        try
        {
            overlapped = entered.Wait(TimeSpan.FromSeconds(10));
        }
        finally
        {
            release.Set();
        }
        var results = await Task.WhenAll(calls);
        Assert.True(overlapped);
        Assert.Equal(2, caches.Distinct(ReferenceEqualityComparer.Instance).Count());
        for (int i = 0; i < results.Length; i++)
        {
            Assert.Contains(results[i].ObjectRefs, reference => reference.Name == $"orders{i}");
        }
    }
}
