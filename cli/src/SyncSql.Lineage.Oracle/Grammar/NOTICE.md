# Vendored grammar

`PlSqlLexer.g4`, `PlSqlParser.g4`, and `PlSqlParserBase.cs` in this folder are vendored, unmodified,
from [antlr/grammars-v4](https://github.com/antlr/grammars-v4)'s `sql/plsql` grammar
(`PlSqlParser.g4`'s own header: Copyright (c) 2009-2011 Alexandre Porcelli, 2015-2019 Ivan Kochurkin
(Positive Technologies), 2017-2018 Mark Adams; licensed under the
[Apache License, Version 2.0](http://www.apache.org/licenses/LICENSE-2.0)).

Only these three files are vendored - nothing generated from them is committed. `Antlr4BuildTasks`
(see `SyncSql.Lineage.Oracle.csproj`) generates and compiles the actual lexer/parser from the `.g4`
files as part of `dotnet build`, the same way any other source file in this project compiles - this is
what makes it possible to verify the grammar actually works (a real `dotnet build`/`dotnet test`, not
tens of thousands of lines of pre-generated code nobody can review or independently confirm compiles).

To pick up a newer upstream grammar revision, replace these three files with the current versions from
the same paths in that repository.
