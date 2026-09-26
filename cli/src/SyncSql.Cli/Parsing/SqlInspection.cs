using System.Collections;
using System.Globalization;
using Antlr4.Runtime;
using Antlr4.Runtime.Tree;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using SyncSql.Core.Domain;
using SyncSql.Lineage.MsSql;
using SyncSql.Lineage.Oracle;

namespace SyncSql.Cli.Parsing;

internal sealed record SqlPiece(string Name, int Offset = 0, int Length = 0, int Line = 0, int Column = 0, string? Description = null);
internal sealed record SqlSection(string Name, IReadOnlyList<SqlPiece> Pieces);

internal sealed record SqlInspection(string Path, string Sql, string Engine, IReadOnlyList<SqlSection> Sections, bool HasErrors)
{
    public string Describe(SqlPiece piece)
    {
        string location = piece.Line > 0 ? string.Create(CultureInfo.InvariantCulture, $"Line {piece.Line}, column {piece.Column}\n") : "";
        int start = Math.Clamp(piece.Offset, 0, Sql.Length);
        int length = Math.Clamp(piece.Length, 0, Sql.Length - start);
        return $"{piece.Name}\n{location}{piece.Description}\n{Sql.Substring(start, length)}";
    }

    public static SqlInspection Parse(string path, string sql, string engine)
    {
        List<SqlPiece> nodes = [];
        List<SqlPiece> tokens = [];
        List<SqlPiece> diagnostics = [];
        LineageAnalysisResult lineage;
        if (engine == "oracle")
        {
            PlSqlLexer lexer = new(new AntlrInputStream(sql));
            OracleErrors errors = new(diagnostics);
            lexer.RemoveErrorListeners();
            lexer.AddErrorListener(errors);
            CommonTokenStream stream = new(lexer);
            PlSqlParser parser = new(stream);
            parser.RemoveErrorListeners();
            parser.AddErrorListener(errors);
            IParseTree root = parser.sql_script();
            Stack<(IParseTree Tree, int Depth)> pending = new();
            pending.Push((root, 0));
            while (pending.TryPop(out var entry))
            {
                if (entry.Tree is ParserRuleContext context)
                {
                    // ANTLR sets Start on every entered rule, including recovered/empty rules.
                    int offset = context.Start.StartIndex;
                    int end = context.Stop?.StopIndex + 1 ?? offset;
                    nodes.Add(new SqlPiece(new string(' ', Math.Min(entry.Depth, 24) * 2) + parser.RuleNames[context.RuleIndex],
                        offset, Math.Max(0, end - offset), context.Start.Line, context.Start.Column + 1));
                }
                for (int i = entry.Tree.ChildCount - 1; i >= 0; i--)
                {
                    pending.Push((entry.Tree.GetChild(i), entry.Depth + 1));
                }
            }
            stream.Fill();
            foreach (IToken token in stream.GetTokens())
            {
                tokens.Add(new SqlPiece(parser.Vocabulary.GetSymbolicName(token.Type), token.StartIndex,
                    Math.Max(0, token.StopIndex - token.StartIndex + 1), token.Line, token.Column + 1,
                    $"Channel: {token.Channel}"));
            }
            lineage = new OracleLineageAnalyzer(NullLogger<OracleLineageAnalyzer>.Instance).Analyze(sql);
        }
        else
        {
            using StringReader reader = new(sql);
            TSqlFragment root = TSqlParserFactory.GetParser().Parse(reader, out IList<ParseError> errors);
            if (errors.Count > 0)
            {
                using StringReader legacyReader = new(sql);
                TSqlFragment legacy = new TSql80Parser(true).Parse(legacyReader, out IList<ParseError> legacyErrors);
                if (legacyErrors.Count == 0)
                {
                    root = legacy;
                    errors = legacyErrors;
                }
            }
            Stack<(TSqlFragment Node, int Depth)> pending = new();
            HashSet<TSqlFragment> seen = new(ReferenceEqualityComparer.Instance);
            pending.Push((root, 0));
            while (pending.TryPop(out var entry))
            {
                if (!seen.Add(entry.Node)) { continue; }
                TSqlFragment node = entry.Node;
                nodes.Add(new SqlPiece(new string(' ', Math.Min(entry.Depth, 24) * 2) + node.GetType().Name,
                    node.StartOffset, node.FragmentLength, node.StartLine, node.StartColumn));
                List<TSqlFragment> children = [];
                foreach (var property in node.GetType().GetProperties())
                {
                    if (property.GetIndexParameters().Length != 0) { continue; }
                    if (typeof(TSqlFragment).IsAssignableFrom(property.PropertyType))
                    {
                        if (property.GetValue(node) is TSqlFragment child) { children.Add(child); }
                    }
                    else if (property.PropertyType.IsGenericType &&
                        property.PropertyType.GetGenericArguments().Any(t => typeof(TSqlFragment).IsAssignableFrom(t)) &&
                        property.GetValue(node) is IEnumerable collection)
                    {
                        children.AddRange(collection.OfType<TSqlFragment>());
                    }
                }
                foreach (TSqlFragment child in children.OrderByDescending(c => c.StartOffset))
                {
                    pending.Push((child, entry.Depth + 1));
                }
            }
            foreach (TSqlParserToken token in root.ScriptTokenStream)
            {
                tokens.Add(new SqlPiece(token.TokenType.ToString(), token.Offset, token.Text?.Length ?? 0, token.Line, token.Column));
            }
            diagnostics.AddRange(errors.Select(e => new SqlPiece($"SQL{e.Number}", e.Offset, 0, e.Line, e.Column, e.Message)));
            lineage = new MsSqlLineageAnalyzer(NullLogger<MsSqlLineageAnalyzer>.Instance).Analyze(sql);
        }

        return new SqlInspection(path, sql, engine,
        [
            new("Syntax tree", nodes),
            new("Tokens", tokens),
            new("Objects", lineage.ObjectRefs.Select(o => new SqlPiece(ObjectName(o), Description: $"Origin: {o.Origin}\nType: {o.ObjectType ?? "unspecified"}\nRoutine: {o.IsRoutine}")).ToArray()),
            new("Aliases", lineage.Aliases.Select(a => new SqlPiece(a.Key, Description: ObjectName(a.Value))).ToArray()),
            new("Columns", lineage.ColumnRefs.Select(c => new SqlPiece($"{c.AliasOrTable}.{c.Column}")).ToArray()),
            new("Diagnostics", diagnostics),
            new("Source", [new SqlPiece(System.IO.Path.GetFileName(path), 0, sql.Length, 1, 1)]),
        ], diagnostics.Count > 0);
    }

    private static string ObjectName(ObjectRef reference) => string.Join('.',
        new[] { reference.Server, reference.Database, reference.Schema, reference.Name }.Where(p => !string.IsNullOrEmpty(p)));

    private sealed class OracleErrors(List<SqlPiece> diagnostics) : BaseErrorListener, IAntlrErrorListener<int>
    {
        public override void SyntaxError(TextWriter output, IRecognizer recognizer, IToken offendingSymbol, int line,
            int charPositionInLine, string msg, RecognitionException e) =>
            diagnostics.Add(new SqlPiece("Parser error", offendingSymbol.StartIndex, 0, line, charPositionInLine + 1, msg));

        public void SyntaxError(TextWriter output, IRecognizer recognizer, int offendingSymbol, int line,
            int charPositionInLine, string msg, RecognitionException e) =>
            diagnostics.Add(new SqlPiece("Lexer error", Line: line, Column: charPositionInLine + 1, Description: msg));
    }
}
