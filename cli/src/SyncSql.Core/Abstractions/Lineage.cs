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

    /// <summary>
    /// Analyzes one object's DDL. <paramref name="options"/> is optional so every existing call site keeps
    /// working and an analyzer that has nothing to configure (Oracle) can simply ignore it.
    /// </summary>
    public LineageAnalysisResult Analyze(string ddl, LineageAnalysisOptions? options = null);
}

/// <summary>
/// Knobs for one analysis pass. Kept as a record rather than constructor-injected options so the same
/// analyzer instance can serve a build that wants dynamic-SQL scanning and one that doesn't, and so a test
/// can flip a switch without going through DI.
/// </summary>
public sealed record LineageAnalysisOptions
{
    /// <summary>
    /// Whether to recover references from SQL built as a string at runtime (see
    /// <see cref="ReferenceOrigin.Dynamic"/>). On by default: a large amount of real T-SQL reaches other
    /// objects only through <c>OPENQUERY</c>/<c>EXEC</c>, and leaving that invisible is a bigger error than
    /// the occasional over-eager match, which is tagged and never reported as an orphan anyway.
    /// </summary>
    public bool DynamicSql { get; init; } = true;

    /// <summary>The current SQL database's Broker identity, when supplied by extraction metadata.</summary>
    public Guid? ServiceBrokerGuid { get; init; }

    public static LineageAnalysisOptions Default { get; } = new();
}

/// <summary>Runtime dispatch from an object's engine to the matching <see cref="ILineageAnalyzer"/> - implemented in SyncSql.Cli via keyed DI, keeping Core free of a DI container reference.</summary>
public interface ILineageAnalyzerResolver
{
    public ILineageAnalyzer Resolve(DatabaseEngine engine);
}
