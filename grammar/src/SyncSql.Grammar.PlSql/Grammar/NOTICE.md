# Vendored grammar

`PlSqlLexer.g4`, `PlSqlParser.g4`, `PlSqlLexerBase.cs`, and `PlSqlParserBase.cs` in this folder are
vendored, unmodified, from [antlr/grammars-v4](https://github.com/antlr/grammars-v4)'s `sql/plsql`
grammar (`PlSqlParser.g4`'s own header: Copyright (c) 2009-2011 Alexandre Porcelli, 2015-2019 Ivan
Kochurkin (Positive Technologies), 2017-2018 Mark Adams; licensed under the
[Apache License, Version 2.0](http://www.apache.org/licenses/LICENSE-2.0), the full text of which is
in `LICENSE.txt` beside this file).

Only these files are vendored - nothing generated from them is committed. `Antlr4BuildTasks` (see
`../SyncSql.Grammar.PlSql.csproj`) generates and compiles the actual lexer/parser from the `.g4`
files as part of `dotnet build`, the same way any other source file compiles - this is what makes it
possible to verify the grammar actually works (a real `dotnet build`, not tens of thousands of lines
of pre-generated code nobody can review or independently confirm compiles).

To pick up a newer upstream grammar revision, replace these files with the current versions from the
same paths in that repository, then publish a new package version - see `../../../README.md`.

## Build prerequisites

Because the parser is generated rather than committed, building **this project** needs a JRE (11+) on
`PATH` - the ANTLR4 tool is a Java program - and the `antlr4-<version>-complete.jar` itself, which
`../AntlrTool.targets` downloads from Maven Central into `~/.m2` on first build. See that file's
header for the `AntlrToolJar` / `AntlrToolJarUrl` / `AntlrToolJarDir` overrides an offline or
mirror-only build needs, and `../../../README.md` for the same thing in prose.

Nothing downstream pays that cost: `SyncSql.Lineage.Oracle` consumes the published
`SyncSql.Grammar.PlSql` package, so building the CLI solution needs only the .NET SDK.
