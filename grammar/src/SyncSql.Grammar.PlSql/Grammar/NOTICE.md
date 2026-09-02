# Vendored grammar

`PlSqlLexer.g4`, `PlSqlParser.g4`, `PlSqlLexerBase.cs`, and `PlSqlParserBase.cs` in this folder are
vendored, unmodified, from [antlr/grammars-v4](https://github.com/antlr/grammars-v4)'s `sql/plsql`
grammar (`PlSqlParser.g4`'s own header: Copyright (c) 2009-2011 Alexandre Porcelli, 2015-2019 Ivan
Kochurkin (Positive Technologies), 2017-2018 Mark Adams; licensed under the
[Apache License, Version 2.0](http://www.apache.org/licenses/LICENSE-2.0), the full text of which is
in `LICENSE.txt` beside this file).

The C# lexer, parser, and visitor under `../Generated/` were generated from these files with ANTLR
4.13.1 and are committed so every build compiles the same reviewed parser without invoking Java or
a code-generation task. The generated code is covered by the same Apache-2.0 license.

To pick up a newer upstream grammar revision, replace these files with the current versions from the
same paths in that repository, regenerate the four C# files, and review both diffs - see
`../../../README.md`.

## Build prerequisites

Building the linked `cli/src/SyncSql.Grammar.PlSql` project needs only the .NET SDK and
`Antlr4.Runtime.Standard`. Java is relevant only to the exceptional maintenance operation of
importing a newer upstream grammar and regenerating the four checked-in C# files; see
`../../../README.md` for the exact command and review workflow.
