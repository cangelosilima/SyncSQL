using Microsoft.SqlServer.TransactSql.ScriptDom;
using SyncSql.Core.Domain;

namespace SyncSql.Lineage.MsSql.Tests;

public sealed class DynamicScannerBoundaryTests
{
    private static IList<TSqlParserToken> Tokens(params string?[] values) => values.Select(v => new TSqlParserToken { Text = v! }).ToList();

    [Theory]
    [InlineData("", 0)]
    [InlineData(" ", 0)]
    [InlineData("-- comment", 0)]
    [InlineData("/* comment */", 0)]
    [InlineData("dbo", 1)]
    [InlineData("[dbo]", 1)]
    [InlineData("\"dbo\"", 1)]
    [InlineData("_name", 1)]
    [InlineData("N", 1)]
    [InlineData("name", 1)]
    [InlineData("N'x'", 2)]
    [InlineData("n'x'", 2)]
    [InlineData("'x'", 2)]
    [InlineData(".", 3)]
    [InlineData("(", 4)]
    [InlineData(")", 5)]
    [InlineData("-", 6)]
    [InlineData("/", 6)]
    [InlineData("1", 6)]
    public void Classify_RecognizesSqlTokenText(string text, int kind) => Assert.Equal(kind, (int)DynamicSqlScanner.Classify(text));

    [Theory]
    [InlineData("x", "x")]
    [InlineData("[a]]b]", "a]b")]
    [InlineData("\"a\"\"b\"", "a\"b")]
    [InlineData("[open", "[open")]
    [InlineData("\"open", "\"open")]
    public void Unquote_PreservesIdentifierEscapes(string text, string expected) => Assert.Equal(expected, DynamicSqlScanner.Unquote(text));

    [Theory]
    [InlineData("'a''b'", "a'b")]
    [InlineData("N'value'", "value")]
    [InlineData("n'value'", "value")]
    [InlineData("'unfinished", "unfinished")]
    [InlineData("'", "")]
    [InlineData("N'", "")]
    [InlineData("''", "")]
    [InlineData("", null)]
    [InlineData("N", null)]
    [InlineData("nothing", null)]
    [InlineData("X'value'", null)]
    public void DecodeLiteral_HandlesPartialStrings(string text, string? expected) => Assert.Equal(expected, DynamicSqlScanner.DecodeStringLiteral(text));

    [Fact]
    public void Names_PreserveOmissionsAndExplicitServerPrecedence()
    {
        Assert.Null(DynamicSqlScanner.ToObjectRef(["dbo", " "], null));
        Assert.Null(DynamicSqlScanner.ToObjectRef(["dbo", "#temp"], null));
        Assert.Equal(new ObjectRef(null, "table") { Origin = ReferenceOrigin.Dynamic }, DynamicSqlScanner.ToObjectRef(["table"], null));
        var reference = DynamicSqlScanner.ToObjectRef(["ignored", "remote", "", "dbo", "table"], "outer")!;
        Assert.Equal("remote", reference.Server);
        Assert.Null(reference.Database);
        Assert.Equal("dbo", reference.Schema);
    }

    [Fact]
    public void OpenQuery_RejectsMissingAndMalformedArguments()
    {
        Assert.False(DynamicSqlScanner.TryReadOpenQueryServer(Tokens("OPENQUERY"), 0, out _));
        Assert.False(DynamicSqlScanner.TryReadOpenQueryServer(Tokens("OPENQUERY", "word"), 0, out _));
        Assert.False(DynamicSqlScanner.TryReadOpenQueryServer(Tokens("OPENQUERY", "("), 0, out _));
        Assert.False(DynamicSqlScanner.TryReadOpenQueryServer(Tokens("OPENQUERY", "(", "'literal'"), 0, out _));
        Assert.False(DynamicSqlScanner.TryReadOpenQueryServer(Tokens("OPENQUERY", "(", "[]"), 0, out _));
        Assert.True(DynamicSqlScanner.TryReadOpenQueryServer(Tokens("OPENQUERY", " ", "(", "[remote]"), 0, out string? name));
        Assert.Equal("remote", name);
        Assert.False(DynamicSqlScanner.FollowsDot(Tokens(" "), 1));
        Assert.True(DynamicSqlScanner.FollowsDot(Tokens(".", " "), 2));
        Assert.False(DynamicSqlScanner.FollowsDot(Tokens((string?)null), 1));
        Assert.False(DynamicSqlScanner.TryReadOpenQueryServer(Tokens("OPENQUERY", null), 0, out _));
    }

    [Fact]
    public void Scan_EnforcesBudgetAndContainsLexerFailures()
    {
        List<ObjectRef> refs = [];
        var budget = new DynamicSqlScanner.Budget();
        for (int i = 0; i < 64; i++) { Assert.True(budget.TryConsume()); }
        Assert.False(budget.TryConsume());
        DynamicSqlScanner.Scan("SELECT * FROM dbo.t", null, 0, budget, refs);
        DynamicSqlScanner.Scan("SELECT * FROM dbo.t", null, 5, new(), refs);
        DynamicSqlScanner.Scan("SELECT * FROM dbo.t", null, 0, new(), refs, _ => throw new InvalidDataException("invalid token stream"));
        Assert.Empty(refs);
    }

    [Fact]
    public void ScanTokens_RecoversFromIncompleteSqlAndTracksRemoteScope()
    {
        List<ObjectRef> refs = [];
        DynamicSqlScanner.ScanTokens(Tokens(null, ".", "AT", "remote", ";", "FROM", "dbo", ".", "orders", ";"), null, 0, new(), refs);
        Assert.Equal("remote", Assert.Single(refs).Server);
        foreach (var tokens in new[] { Tokens("AT"), Tokens("AT", "'x'"), Tokens("AT", "TIME"), Tokens("OPENQUERY"), Tokens("FROM", "dbo", ".") })
        {
            DynamicSqlScanner.ScanTokens(tokens, null, 0, new(), refs);
        }
        Assert.Single(refs);
        DynamicSqlScanner.ScanTokens(Tokens("FROM", "dbo", ".", "[]"), null, 0, new(), refs);
        Assert.Single(refs);
        DynamicSqlScanner.Scan("SELECT * FROM OPENQUERY(remote, 'SELECT * FROM dbo.orders'); SELECT * FROM dbo.local", null, 0, new(), refs);
        Assert.Contains(refs, r => r.Name == "local" && r.Server is null);
        Assert.Contains(refs, r => r.Name == "orders" && r.Server == "remote");
        DynamicSqlScanner.Scan("BEGIN app.pkg.run; END;", "oracle", 0, new(), refs);
        Assert.Contains(refs, r => r.Name == "run" && r.IsRoutine && r.Server == "oracle");
        DynamicSqlScanner.Scan("SELECT * FROM remote..dbo.orders;", null, 0, new(), refs);
        Assert.Contains(refs, r => r.Name == "orders" && r.Server == "remote" && r.Database is null);
    }
}
