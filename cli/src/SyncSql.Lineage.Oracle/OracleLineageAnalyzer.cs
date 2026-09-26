#nullable enable
using Antlr4.Runtime;
using Antlr4.Runtime.Atn;
using Antlr4.Runtime.Dfa;
using Microsoft.Extensions.Logging;
using SyncSql.Core.Abstractions;
using SyncSql.Core.Domain;

namespace SyncSql.Lineage.Oracle;

/// <summary>
/// Real PL/SQL-grammar-based lineage inference for Oracle objects (the vendored antlr/grammars-v4
/// PlSqlLexer/PlSqlParser, compiled from the generated C# checked into grammar/ - see
/// grammar/README.md at the repository root), replacing the comment/string-scrubbing-plus-regex
/// approach the original PowerShell pipeline used as an interim fix. A real parse tree naturally can't
/// mistake string-literal or comment content for identifiers (the lexer simply never tokenizes it as
/// one), including Oracle's q'...' alternative quoting, which the old regex-based approach had no way
/// to recognize at all.
/// </summary>
public sealed class OracleLineageAnalyzer : ILineageAnalyzer
{
    private readonly ILogger<OracleLineageAnalyzer> _logger;
    private readonly Func<PlSqlParser, PlSqlParser.Sql_scriptContext> _parse;
    private PredictionCaches? _idleCache;
    internal const int MaxCachedScripts = 16;
    internal const int MaxCachedCharacters = 1024 * 1024;
    internal const int MaxCachedStates = 2048;
    internal const int MaxCachedConfigurations = 250_000;
    internal const int MaxCachedContexts = 16_384;

    public OracleLineageAnalyzer(ILogger<OracleLineageAnalyzer> logger)
        : this(logger, parser => parser.sql_script()) { }

    internal OracleLineageAnalyzer(ILogger<OracleLineageAnalyzer> logger, Func<PlSqlParser, PlSqlParser.Sql_scriptContext> parse)
    {
        _logger = logger;
        _parse = parse;
    }

    public DatabaseEngine Engine => DatabaseEngine.Oracle;

    /// <summary>
    /// Literal EXECUTE IMMEDIATE and OPEN FOR statements, including constant string
    /// concatenations, are parsed with the Oracle grammar and retain column bindings.
    /// Nested scans have a shared count budget and depth/size bounds.
    /// </summary>
    public LineageAnalysisResult Analyze(string ddl, LineageAnalysisOptions? options = null)
    {
        // Lease the one idle cache exclusively. Concurrent callers get independent caches;
        // neither interpreters, token streams nor trees are retained between calls.
        PredictionCaches cache = Interlocked.Exchange(ref _idleCache, null) ?? new();
        bool completed = false;
        try
        {
            LineageAnalysisResult result = AnalyzeCore(ddl, options ?? LineageAnalysisOptions.Default, 0, new DynamicBudget(), cache);
            completed = true;
            return result;
        }
        finally
        {
            if (completed && cache.CanReuse())
            {
                Interlocked.CompareExchange(ref _idleCache, cache, null);
            }
        }
    }

    private sealed class DynamicBudget
    {
        public int Remaining { get; set; } = 64;
    }

    private LineageAnalysisResult AnalyzeCore(string ddl, LineageAnalysisOptions options, int depth, DynamicBudget budget, PredictionCaches cache)
    {
        if (string.IsNullOrWhiteSpace(ddl))
        {
            return LineageAnalysisResult.Empty;
        }

        try
        {
            cache.Scripts++;
            cache.Characters += ddl.Length;
            AntlrInputStream inputStream = new(ddl);
            PlSqlLexer lexer = new LocalCacheLexer(inputStream, cache);
            CollectingErrorListener errorListener = new();
            lexer.RemoveErrorListeners();
            lexer.AddErrorListener(errorListener);

            CommonTokenStream tokenStream = new(lexer);
            PlSqlParser parser = new LocalCacheParser(tokenStream, cache);
            parser.RemoveErrorListeners();
            parser.AddErrorListener(errorListener);

            PlSqlParser.Sql_scriptContext tree = _parse(parser);

            if (errorListener.Errors.Count > 0)
            {
                _logger.LogWarning("[{ObjectId}] PL/SQL parse produced {Count} error(s) (continuing with the partial tree): {Message}",
                    options.SourceObjectId, errorListener.Errors.Count, errorListener.Errors[0]);
            }

            PlSqlLineageVisitor visitor = new(sql =>
                depth < 4 && sql.Length <= 65536 && budget.Remaining-- > 0
                    ? AnalyzeCore(sql.EndsWith(';') ? sql : sql + ";", options, depth + 1, budget, cache)
                    : LineageAnalysisResult.Empty);
            visitor.Visit(tree);

            return new LineageAnalysisResult
            {
                ObjectRefs = visitor.ObjectRefs,
                Aliases = visitor.Aliases,
                ColumnRefs = visitor.ColumnRefs,
            };
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            cache.Failed = true;
            _logger.LogWarning("[{ObjectId}] PL/SQL parsing failed (skipping lineage for this object): {Message}", options.SourceObjectId, ex.Message);
            return LineageAnalysisResult.Empty;
        }
    }

    // A cold PL/SQL DFA is expensive to construct. Reuse it briefly, with independent limits
    // on lifetime, input volume, states, configurations and shared prediction contexts.
    // Limits govern retention BETWEEN objects; a single complex parse can still exceed them.
    private sealed class PredictionCaches
    {
        public DFA[]? Lexer { get; set; }
        public DFA[]? Parser { get; set; }
        public PredictionContextCache LexerContexts { get; } = new();
        public PredictionContextCache ParserContexts { get; } = new();
        public int Scripts { get; set; }
        public long Characters { get; set; }
        public bool Failed { get; set; }

        public bool CanReuse()
        {
            if (Failed || Scripts >= MaxCachedScripts || Characters >= MaxCachedCharacters
                || LexerContexts.Count + ParserContexts.Count > MaxCachedContexts)
            {
                return false;
            }
            int states = 0;
            long configurations = 0;
            foreach (DFA decision in (Lexer ?? []).Concat(Parser ?? []))
            {
                states += decision.states.Count;
                if (states > MaxCachedStates)
                {
                    return false;
                }
                foreach (DFAState state in decision.states.Keys)
                {
                    configurations += state.configSet.Count;
                    if (configurations > MaxCachedConfigurations)
                    {
                        return false;
                    }
                }
            }
            return true;
        }
    }

    private sealed class LocalCacheLexer : PlSqlLexer
    {
        public LocalCacheLexer(ICharStream input, PredictionCaches cache) : base(input)
        {
            Interpreter = new LexerATNSimulator(this, Atn, cache.Lexer ??= CreateDecisionCache(Atn), cache.LexerContexts);
        }
    }

    private sealed class LocalCacheParser : PlSqlParser
    {
        public LocalCacheParser(ITokenStream input, PredictionCaches cache) : base(input)
        {
            Interpreter = new ParserATNSimulator(this, Atn, cache.Parser ??= CreateDecisionCache(Atn), cache.ParserContexts);
        }
    }

    private static DFA[] CreateDecisionCache(ATN atn)
    {
        DFA[] decisions = new DFA[atn.NumberOfDecisions];
        for (int i = 0; i < decisions.Length; i++)
        {
            decisions[i] = new DFA(atn.GetDecisionState(i), i);
        }
        return decisions;
    }
}
