using BenchmarkDotNet.Attributes;
using SyncSql.Core.Abstractions;
using SyncSql.Core.Domain;

namespace SyncSql.Benchmarks;

public class ParserBenchmarks
{
    private ILineageAnalyzer _analyzer = null!;
    private string[] _scripts = [];

    [Params(DatabaseEngine.MsSql, DatabaseEngine.Oracle)]
    public DatabaseEngine Engine { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _analyzer = BenchmarkWorkload.CreateAnalyzer(Engine);
        _scripts = [.. Enumerable.Range(0, 16).Select(i => i % 4 == 3
            ? Engine == DatabaseEngine.Oracle
                ? "BEGIN EXECUTE IMMEDIATE 'SELECT t.Id FROM APP.V000 t'; END;"
                : "EXEC ('SELECT t.Id FROM APP.V000 t');"
            : BenchmarkWorkload.SelectSql(i))];
    }

    [Benchmark]
    public int AnalyzeBatch()
    {
        int references = 0;
        for (int i = 0; i < _scripts.Length; i++)
        {
            LineageAnalysisResult analysis = _analyzer.Analyze(_scripts[i]);
            bool dynamic = i % 4 == 3;
            // Dynamic scanners recover object references; static statements also bind columns.
            if (!analysis.ObjectRefs.Any(reference => reference.Name == "V000")
                || (dynamic && !analysis.ObjectRefs.Any(reference => reference.Origin == ReferenceOrigin.Dynamic))
                || (!dynamic && !analysis.ColumnRefs.Any(reference => reference.Column == "Id")))
            {
                throw new InvalidOperationException("Benchmark parser lost expected table or column references.");
            }
            references += analysis.ObjectRefs.Count;
        }
        return references;
    }
}
