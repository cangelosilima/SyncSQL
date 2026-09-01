# `SyncSql.Grammar.PlSql`

The PL/SQL lexer and parser that `SyncSql.Lineage.Oracle` analyzes Oracle DDL with, built
once and published to the Nexus feed as an ordinary NuGet package.

## Why this is a separate package

Generating a parser from an ANTLR4 `.g4` grammar means running the ANTLR4 tool, which is a
Java program. Nobody wants a JRE, a downloaded jar, or 300k lines of generated code standing
between them and `dotnet build`.

So the Java step lives here and only here, and it runs roughly never - only when the grammar
itself changes. Everything downstream consumes the result as a package:

```
grammar/src/SyncSql.Grammar.PlSql   .g4 grammar -> ANTLR4 (Java) -> SyncSql.Grammar.PlSql.nupkg
                                     |  built by hand, published to Nexus
                                     v
cli/src/SyncSql.Lineage.Oracle       <PackageReference Include="SyncSql.Grammar.PlSql" />
```

This project is deliberately **not** in `cli/SyncSql.slnx`. `dotnet build cli/SyncSql.slnx`,
`dotnet test`, and the whole `cli-*` CI pipeline need the .NET SDK and nothing else - no Java,
no jar, and no generated code in source control.

## What's in `Grammar/`

`PlSqlLexer.g4`, `PlSqlParser.g4`, and `PlSqlParserBase.cs`/`PlSqlLexerBase.cs` are vendored
unmodified from [antlr/grammars-v4](https://github.com/antlr/grammars-v4)'s `sql/plsql`
grammar, Apache-2.0. See [`src/SyncSql.Grammar.PlSql/Grammar/NOTICE.md`](src/SyncSql.Grammar.PlSql/Grammar/NOTICE.md)
for the full attribution and how to pick up a newer upstream revision.

The generated `PlSqlLexer.cs`/`PlSqlParser.cs`/visitors are build output. They are never
committed - they exist only inside `obj/` on whoever's machine last built this, and inside
the published package.

## Prerequisites (only for building *this* project)

- **.NET SDK.** The project targets `netstandard2.0` so it isn't tied to the CLI's framework.
- **A JRE, 11 or newer, on `PATH`** - the ANTLR4 tool is a Java program.
  (`apt-get install -y default-jre-headless`, `brew install openjdk`, or
  `winget install Microsoft.OpenJDK.21`.)
- **The ANTLR4 tool jar.** [`src/SyncSql.Grammar.PlSql/AntlrTool.targets`](src/SyncSql.Grammar.PlSql/AntlrTool.targets)
  downloads `antlr4-4.13.1-complete.jar` from Maven Central into `~/.m2` on the first build
  and reuses it afterwards.

On a machine that can't reach Maven Central, that download is the step that fails. Any of
these gets you past it (each is an MSBuild property, so an environment variable of the same
name works too):

```bash
# a jar you already have - no download at all
dotnet build -p:AntlrToolJar=/path/to/antlr4-4.13.1-complete.jar

# an internal mirror (Nexus/Artifactory) instead of Maven Central
dotnet build -p:AntlrToolJarUrl=https://nexus.example/repository/maven/org/antlr/antlr4/4.13.1/antlr4-4.13.1-complete.jar

# somewhere other than ~/.m2 to cache the download
dotnet build -p:AntlrToolJarDir=/var/cache/antlr
```

Without one of those the build stops with a message naming all three. Letting
`Antlr4BuildTasks` fall back to its own probing instead is what produces the unhelpful
`Went through the complete probe list looking for an Antlr4 tool jar` failure.

## Publishing a new version

Only needed when `Grammar/*.g4` changes.

1. Bump `<Version>` in
   [`src/SyncSql.Grammar.PlSql/SyncSql.Grammar.PlSql.csproj`](src/SyncSql.Grammar.PlSql/SyncSql.Grammar.PlSql.csproj).
   It versions independently of the CLI - this package only moves when the grammar does.
2. Run the **`grammar-publish`** job (manual, on the default branch - see
   [`.gitlab/ci/grammar.yml`](../.gitlab/ci/grammar.yml)). It installs a JRE, packs, and
   pushes to `$NEXUS_NUGET_SOURCE_URL`.

   Or locally, if you have Java and push rights:

   ```bash
   dotnet pack grammar/src/SyncSql.Grammar.PlSql -c Release -o ./nupkg
   dotnet nuget push "./nupkg/*.nupkg" --source <nexus-nuget-feed-url> --api-key <key>
   ```
3. Bump `<PlSqlGrammarVersion>` in
   [`cli/src/SyncSql.Lineage.Oracle/SyncSql.Lineage.Oracle.csproj`](../cli/src/SyncSql.Lineage.Oracle/SyncSql.Lineage.Oracle.csproj)
   to match, and let the `cli-test` job confirm the CLI still builds and its lineage tests
   still pass against the new parser.

Steps 1-2 and step 3 are separate commits' worth of work on purpose: the package has to exist
on the feed before anything referencing that version can restore.

## A note on namespaces

The generated types (`PlSqlLexer`, `PlSqlParser`, `PlSqlParserBaseVisitor<T>`, ...) live in the
**global namespace**, not under `SyncSql.Grammar.PlSql`. That is not an oversight: the vendored
`PlSqlLexerBase`/`PlSqlParserBase` reference the generated types unqualified, so putting the
generated code in a namespace would mean editing files we deliberately keep byte-identical to
upstream. It matches how the upstream grammar's own C# target builds, and how
`SyncSql.Lineage.Oracle` already referred to these types back when they were generated in place.
