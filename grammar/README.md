# `SyncSql.Grammar.PlSql`

The complete Oracle SQL and PL/SQL parser used by `SyncSql.Lineage.Oracle`.

## Java-free build design

The human-readable grammar remains the Apache-2.0 `sql/plsql` grammar from
[`antlr/grammars-v4`](https://github.com/antlr/grammars-v4). ANTLR 4.13.1 generated the C# lexer,
parser, and visitor once; those generated `.cs` files are committed under `Generated/`.

Normal development is therefore an ordinary .NET build:

```text
Grammar/*.g4 ── documents the grammar and supports future upgrades
Generated/*.cs ── compiled directly by the .NET SDK
Antlr4.Runtime.Standard ── the only ANTLR package required
```

`dotnet build cli/SyncSql.slnx`, tests, CI, and `dotnet pack` do not run a generator and do not
need Java, an ANTLR tool JAR, a private `SyncSql.Grammar.PlSql` package, or a Nexus restore source.
The thin `cli/src/SyncSql.Grammar.PlSql` compile project links these sources and is referenced
directly by `SyncSql.Lineage.Oracle`; the resulting assembly is included in the `SyncSql.Cli` tool
package.

This deliberately favors a larger source checkout over a hidden build-time toolchain. It preserves
the mature Oracle/PLSQL coverage already exercised by the lineage visitor, while pure-C# SQL parser
libraries currently omit substantial Oracle procedural syntax and Tree-sitter grammars prioritize
error-tolerant editor parsing rather than complete validation/analysis coverage.

## Layout

- `Grammar/PlSqlLexer.g4` and `Grammar/PlSqlParser.g4`: vendored grammar source.
- `Grammar/PlSqlLexerBase.cs` and `Grammar/PlSqlParserBase.cs`: vendored C# target support.
- `Generated/PlSqlLexer.cs`: generated lexer.
- `Generated/PlSqlParser.cs`: generated parser.
- `Generated/PlSqlParserVisitor.cs` and `Generated/PlSqlParserBaseVisitor.cs`: generated visitor API.
- `../cli/src/SyncSql.Grammar.PlSql/`: Java-free compile project that links the files above.

Generated types intentionally remain in the global namespace. The upstream base classes reference
them without qualification, and the existing Oracle lineage analyzer consumes the same API.

## Updating the upstream grammar

Java is not a repository build dependency. A maintainer only needs the ANTLR generator when choosing
to import a newer upstream grammar revision. In that exceptional maintenance workflow:

1. Replace the four vendored files in `Grammar/` with a reviewed upstream revision.
2. Generate the C# artifacts with ANTLR 4.13.1 (visitor enabled, listener disabled):

   ```bash
   java -jar antlr4-4.13.1-complete.jar \
     -Dlanguage=CSharp -visitor -no-listener -Xexact-output-dir \
     -o Generated Grammar/PlSqlLexer.g4 Grammar/PlSqlParser.g4
   ```

3. Commit only the four generated `.cs` files. `.interp` and `.tokens` are generator diagnostics and
   are ignored.
4. Run `dotnet test cli/SyncSql.slnx --configuration Release` and review the generated diff together
   with the grammar change.

Keeping generation explicit prevents an unreviewed grammar update from silently changing hundreds
of thousands of generated lines during an unrelated build.
