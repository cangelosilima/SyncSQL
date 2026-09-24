using Antlr4.Runtime;
using Microsoft.Extensions.Logging.Abstractions;
using SyncSql.Core.Abstractions;

namespace SyncSql.Lineage.Oracle.Tests;

public sealed class ParserMemoryTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Analyze_PredictionCachesAreCollectibleAfterEachObject(bool failParsing)
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

        for (int i = 0; i < 3; i++)
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
        Assert.Equal(6, caches.Count);
        Assert.All(caches, cache => Assert.False(cache.IsAlive));
        GC.KeepAlive(analyzer);
    }
}
