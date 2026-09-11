using Microsoft.SqlServer.TransactSql.ScriptDom;
using SyncSql.Core.Domain;

namespace SyncSql.Lineage.MsSql;

/// <summary>
/// Recovers object references from SQL that only exists as a string until runtime - the
/// <c>DECLARE @sql = '...' + @id + '...'; EXEC (@sql) AT LNK</c> and
/// <c>OPENQUERY(LNK, '...')</c> idioms that a large amount of real T-SQL depends on, and that the AST
/// walk in <see cref="TSqlLineageVisitor"/> is structurally incapable of seeing.
///
/// <para><b>Why this lexes rather than parses.</b> The obvious approach - re-parse the literal with
/// ScriptDom and walk the result - fails on the shape that matters most. In
/// <c>'SELECT dbo.Fn(' + @id + ') AS x'</c> the interpolated variable splits the statement across two
/// literals, so neither half is parseable T-SQL on its own and the assembled text isn't either (the
/// runtime value is unknown). Insisting on a clean parse would therefore find nothing in exactly the
/// cases this exists for. Tokenizing does work: ScriptDom's own lexer still divides an incomplete
/// fragment into real identifiers, strings and comments, which is all that is needed to spot a
/// schema-qualified name - and it is a great deal more principled than a regex, because it cannot
/// confuse a name inside a comment or a nested literal for a reference.</para>
///
/// <para><b>What it deliberately will not do.</b> Only schema-qualified names in an actual reference
/// position (after FROM/JOIN/EXEC/... or immediately before an opening parenthesis) are collected. A bare
/// name is indistinguishable from a column, an alias or a keyword without a binder, and guessing at one
/// would bury the real findings - the same "don't guess" posture the static resolver already takes. Every
/// reference produced here is tagged <see cref="ReferenceOrigin.Dynamic"/>, which is what keeps it out of
/// the orphaned-reference list when it resolves to nothing.</para>
/// </summary>
internal static class DynamicSqlScanner
{
    /// <summary>
    /// How deep a literal-inside-a-literal chain is followed. <c>OPENQUERY(LNK, '... ''inner'' ...')</c>
    /// is already two, so the limit has to be above that; past a handful it is generated SQL nobody is
    /// going to read a lineage graph for, and each level multiplies the work.
    /// </summary>
    private const int MaxDepth = 4;

    /// <summary>
    /// Total nested scans allowed for one object, shared across the whole walk. A procedure that builds
    /// SQL in a loop can carry hundreds of literals, and catalog build runs over every object in a fleet.
    /// </summary>
    private const int MaxScansPerObject = 64;

    /// <summary>Below this a literal cannot hold a qualified reference, so tokenizing it is pure cost.</summary>
    private const int MinInterestingLength = 8;

    /// <summary>
    /// Keywords after which a name is being referenced rather than merely mentioned. This is what stops
    /// <c>o.OrderId</c> in a dynamically-built SELECT list from being read as a reference to a schema
    /// called <c>o</c>: a column reference never follows one of these.
    /// </summary>
    private static readonly string[] ReferencePositionKeywords =
    [
        "FROM", "JOIN", "INTO", "UPDATE", "TABLE", "APPLY", "MERGE", "EXEC", "EXECUTE", "REFERENCES",
    ];

    /// <summary>Statement keywords that make a following qualified name a reference on their own ("DELETE dbo.T").</summary>
    private static readonly string[] SqlKeywordsWorthScanningFor =
    [
        "SELECT", "INSERT", "UPDATE", "DELETE", "MERGE", "EXEC", "EXECUTE", "FROM", "JOIN", "OPENQUERY", "BEGIN",
    ];

    /// <summary>Tracks the per-object scan budget across the whole nested walk.</summary>
    internal sealed class Budget
    {
        private int _remaining = MaxScansPerObject;

        public bool TryConsume()
        {
            if (_remaining <= 0)
            {
                return false;
            }
            _remaining--;
            return true;
        }
    }

    /// <summary>
    /// Scans one piece of dynamically-built SQL, appending everything it recognizes to
    /// <paramref name="into"/>.
    /// </summary>
    /// <param name="sql">The SQL text, already decoded (no surrounding quotes, <c>''</c> collapsed).</param>
    /// <param name="linkedServer">The linked server this text executes on, when an enclosing <c>OPENQUERY</c>/<c>AT</c> established one.</param>
    /// <param name="depth">Current literal nesting depth; the initial call passes 0.</param>
    /// <param name="budget">Shared per-object budget.</param>
    /// <param name="into">Collected references.</param>
    public static void Scan(string? sql, string? linkedServer, int depth, Budget budget, List<ObjectRef> into)
    {
        if (string.IsNullOrWhiteSpace(sql) || sql.Length < MinInterestingLength || depth > MaxDepth)
        {
            return;
        }

        // A cheap keyword sniff before paying for the lexer. Prose, a message template or a comma-separated
        // list is not dynamic SQL, and the overwhelming majority of literals in real DDL are one of those.
        if (!LooksLikeSql(sql) || !budget.TryConsume())
        {
            return;
        }

        IList<TSqlParserToken> tokens;
        try
        {
            using StringReader reader = new(sql);
            tokens = TSqlParserFactory.GetParser().GetTokenStream(reader, out _);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Lexing a fragment that was never valid T-SQL is an expected outcome here, not a fault worth
            // reporting: the caller simply learns nothing from this literal.
            return;
        }

        ScanTokens(tokens, linkedServer, depth, budget, into);
    }

    private static void ScanTokens(IList<TSqlParserToken> tokens, string? inheritedServer, int depth, Budget budget, List<ObjectRef> into)
    {
        // The linked server an enclosing OPENQUERY(...) argument list establishes, and the parenthesis
        // depth it was opened at, so it stops applying at the matching close.
        string? openQueryServer = null;
        int openQueryDepth = -1;
        int parenDepth = 0;

        // "EXEC (@sql) AT LNK" names its server after the statement body rather than around it, so unlike
        // OPENQUERY it cannot be scoped by parentheses. It applies to the rest of this fragment, which for
        // a fragment that is one statement is exactly right.
        string? atServer = null;

        // Whether the last meaningful token puts a name in a reference position.
        bool referencePosition = false;
        bool routinePosition = false;

        List<string> parts = [];

        for (int i = 0; i < tokens.Count; i++)
        {
            string text = tokens[i].Text ?? "";
            TokenKind kind = Classify(text);
            if (kind == TokenKind.Trivia)
            {
                continue;
            }

            if (kind == TokenKind.StringLiteral)
            {
                FlushName();
                // The nested query text carries the server established around it - that is what turns
                // OPENQUERY(SQL_A, '...dbo.Fn(...') into a reference to dbo.Fn *on SQL_A* rather than here.
                Scan(DecodeStringLiteral(text), openQueryServer ?? atServer ?? inheritedServer, depth + 1, budget, into);
                referencePosition = false;
                continue;
            }

            if (kind == TokenKind.Dot)
            {
                // Only meaningful while a name is being assembled; a leading dot is not.
                if (parts.Count == 0)
                {
                    continue;
                }
                // "Srv..dbo.Orders" - an omitted part is legal and has to keep its position.
                if (Peek(tokens, i + 1) is { } next && Classify(next) == TokenKind.Dot)
                {
                    parts.Add("");
                }
                continue;
            }

            if (kind == TokenKind.Word)
            {
                string word = Unquote(text);

                // A name only continues across a dot; anything else starts a fresh one.
                if (parts.Count > 0 && !FollowsDot(tokens, i))
                {
                    FlushName();
                }

                if (parts.Count == 0 && (IsKeyword(word, ReferencePositionKeywords)
                    || (inheritedServer is not null && word.Equals("BEGIN", StringComparison.OrdinalIgnoreCase))))
                {
                    referencePosition = true;
                    // Remote PL/SQL permits a procedure call after BEGIN without parentheses.
                    routinePosition = word.Equals("BEGIN", StringComparison.OrdinalIgnoreCase);
                    continue;
                }

                if (parts.Count == 0 && word.Equals("AT", StringComparison.OrdinalIgnoreCase))
                {
                    // "EXEC (@sql) AT LNK" names the server the batch runs on. "x AT TIME ZONE 'UTC'" is
                    // the one other thing AT introduces in T-SQL, and it is not a server.
                    if (Peek(tokens, i + 1) is { } server
                        && Classify(server) == TokenKind.Word
                        && !server.Equals("TIME", StringComparison.OrdinalIgnoreCase))
                    {
                        atServer = Unquote(server);
                    }
                    continue;
                }

                if (parts.Count == 0 && word.Equals("OPENQUERY", StringComparison.OrdinalIgnoreCase))
                {
                    if (TryReadOpenQueryServer(tokens, i, out string? server))
                    {
                        openQueryServer = server;
                        openQueryDepth = parenDepth + 1;
                    }
                    continue;
                }

                parts.Add(word);
                continue;
            }

            if (kind == TokenKind.OpenParen)
            {
                parenDepth++;
                // "dbo.Fn(" - a call is a reference to the function whatever preceded it, which is how a
                // scalar function invoked in a SELECT list gets found.
                FlushName(calledAsFunction: true);
                continue;
            }

            if (kind == TokenKind.CloseParen)
            {
                FlushName();
                parenDepth--;
                if (openQueryServer is not null && parenDepth < openQueryDepth)
                {
                    openQueryServer = null;
                    openQueryDepth = -1;
                }
                continue;
            }

            FlushName();
        }

        FlushName();

        void FlushName(bool calledAsFunction = false)
        {
            if (parts.Count == 0)
            {
                referencePosition = false;
                routinePosition = false;
                return;
            }

            // A bare name is too weak to act on - it could be a column, an alias, a variable or a keyword -
            // so only qualified names count, exactly as the static resolver refuses to widen a bare name
            // across a server boundary.
            if ((referencePosition || calledAsFunction) && parts.Count >= 2)
            {
                if (ToObjectRef(parts, openQueryServer ?? atServer ?? inheritedServer) is { } objectRef)
                {
                    into.Add(objectRef with { IsRoutine = calledAsFunction || routinePosition });
                }
            }

            parts.Clear();
            referencePosition = false;
            routinePosition = false;
        }
    }

    /// <summary>Maps the 2- to 4-part name just read onto an <see cref="ObjectRef"/>, tagged as dynamic.</summary>
    private static ObjectRef? ToObjectRef(List<string> parts, string? linkedServer)
    {
        // More than four parts is not a T-SQL name; the trailing four are the ones that mean anything.
        List<string> tail = parts.Count > 4 ? parts.GetRange(parts.Count - 4, 4) : parts;
        string name = tail[^1];
        if (string.IsNullOrWhiteSpace(name) || name.StartsWith('#'))
        {
            return null;
        }

        string? schema = tail.Count >= 2 ? Blank(tail[^2]) : null;
        string? database = tail.Count >= 3 ? Blank(tail[^3]) : null;
        string? server = tail.Count >= 4 ? Blank(tail[^4]) : null;

        return new ObjectRef(schema, name)
        {
            Database = database,
            // A server spelled out in the name itself wins: it is what the dynamic SQL actually says,
            // whereas the enclosing OPENQUERY only says where the statement runs.
            Server = server ?? Blank(linkedServer),
            Origin = ReferenceOrigin.Dynamic,
        };

        static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
    }

    /// <summary>Reads the linked-server argument out of <c>OPENQUERY(&lt;server&gt;, ...)</c>.</summary>
    private static bool TryReadOpenQueryServer(IList<TSqlParserToken> tokens, int openQueryIndex, out string? server)
    {
        server = null;
        int index = NextMeaningful(tokens, openQueryIndex + 1);
        if (index < 0 || Classify(tokens[index].Text ?? "") != TokenKind.OpenParen)
        {
            return false;
        }

        index = NextMeaningful(tokens, index + 1);
        if (index < 0 || Classify(tokens[index].Text ?? "") != TokenKind.Word)
        {
            return false;
        }

        server = Unquote(tokens[index].Text ?? "");
        return !string.IsNullOrWhiteSpace(server);
    }

    /// <summary>True when the token before <paramref name="index"/> (ignoring trivia) is a dot.</summary>
    private static bool FollowsDot(IList<TSqlParserToken> tokens, int index)
    {
        for (int i = index - 1; i >= 0; i--)
        {
            TokenKind kind = Classify(tokens[i].Text ?? "");
            if (kind == TokenKind.Trivia)
            {
                continue;
            }
            return kind == TokenKind.Dot;
        }
        return false;
    }

    private static string? Peek(IList<TSqlParserToken> tokens, int index)
    {
        int next = NextMeaningful(tokens, index);
        return next < 0 ? null : tokens[next].Text;
    }

    private static int NextMeaningful(IList<TSqlParserToken> tokens, int index)
    {
        for (int i = index; i < tokens.Count; i++)
        {
            if (Classify(tokens[i].Text ?? "") != TokenKind.Trivia)
            {
                return i;
            }
        }
        return -1;
    }

    private static bool IsKeyword(string word, string[] keywords) =>
        keywords.Contains(word, StringComparer.OrdinalIgnoreCase);

    private static bool LooksLikeSql(string sql) =>
        SqlKeywordsWorthScanningFor.Any(keyword => sql.Contains(keyword, StringComparison.OrdinalIgnoreCase));

    private enum TokenKind
    {
        Trivia,
        Word,
        StringLiteral,
        Dot,
        OpenParen,
        CloseParen,
        Other,
    }

    /// <summary>
    /// Classifies a token by its source text rather than by <c>TSqlParserToken.TokenType</c>. The lexer has
    /// already done the hard part - deciding where a string, comment or identifier begins and ends - and
    /// the first character of the resulting text is then unambiguous in T-SQL. Reading it this way keeps
    /// this file independent of the (large, version-dependent) token-type enum.
    /// </summary>
    private static TokenKind Classify(string text)
    {
        if (text.Length == 0 || string.IsNullOrWhiteSpace(text))
        {
            return TokenKind.Trivia;
        }

        char first = text[0];
        return first switch
        {
            '.' => TokenKind.Dot,
            '(' => TokenKind.OpenParen,
            ')' => TokenKind.CloseParen,
            '\'' => TokenKind.StringLiteral,
            '[' or '"' => TokenKind.Word,
            '-' when text.StartsWith("--", StringComparison.Ordinal) => TokenKind.Trivia,
            '/' when text.StartsWith("/*", StringComparison.Ordinal) => TokenKind.Trivia,
            'N' or 'n' when text.Length > 1 && text[1] == '\'' => TokenKind.StringLiteral,
            _ when char.IsLetter(first) || first == '_' => TokenKind.Word,
            _ => TokenKind.Other,
        };
    }

    /// <summary>Strips <c>[]</c>/<c>""</c> delimiters so a quoted identifier compares equal to a plain one.</summary>
    private static string Unquote(string text)
    {
        if (text.Length >= 2)
        {
            if (text[0] == '[' && text[^1] == ']')
            {
                return text[1..^1].Replace("]]", "]", StringComparison.Ordinal);
            }
            if (text[0] == '"' && text[^1] == '"')
            {
                return text[1..^1].Replace("\"\"", "\"", StringComparison.Ordinal);
            }
        }
        return text;
    }

    /// <summary>
    /// Turns a string-literal token back into the text it stands for: delimiters off, doubled quotes
    /// collapsed. Working from raw token text (rather than <c>StringLiteral.Value</c>) is what lets the same
    /// decoding serve both the AST entry points and literals found nested inside another literal.
    /// </summary>
    internal static string? DecodeStringLiteral(string text)
    {
        int start = text.StartsWith('\'') ? 1
            : text.Length > 1 && (text[0] is 'N' or 'n') && text[1] == '\'' ? 2
            : -1;
        if (start < 0)
        {
            return null;
        }

        // An unterminated literal is normal here - assembling a concatenation drops the runtime values that
        // would have closed it - so the closing quote is taken off only when there is one.
        int end = text.Length > start && text[^1] == '\'' ? text.Length - 1 : text.Length;
        return end <= start ? "" : text[start..end].Replace("''", "'", StringComparison.Ordinal);
    }
}
