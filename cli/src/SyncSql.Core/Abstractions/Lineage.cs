using SyncSql.Core.Domain;

namespace SyncSql.Core.Abstractions;

/// <summary>
/// Analyzes one object's DDL text for lineage - other objects it references, FROM-clause alias
/// bindings, and column references - one implementation per <see cref="DatabaseEngine"/>. Pure and
/// synchronous: no I/O, no DB access, just text/AST in and a <see cref="LineageAnalysisResult"/> out,
/// which is what makes these fully unit-testable without any external dependency.
/// </summary>
public interface ILineageAnalyzer
{
    public DatabaseEngine Engine { get; }

    public LineageAnalysisResult Analyze(string ddl);
}

/// <summary>Runtime dispatch from an object's engine to the matching <see cref="ILineageAnalyzer"/> - implemented in SyncSql.Cli via keyed DI, keeping Core free of a DI container reference.</summary>
public interface ILineageAnalyzerResolver
{
    public ILineageAnalyzer Resolve(DatabaseEngine engine);
}
