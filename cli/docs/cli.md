# syncsql CLI

`syncsql` is a cross-platform .NET 10 command-line tool that extracts
database objects from a fleet of MSSQL and Oracle servers, infers
lineage between them, and builds the `catalog.json` document the
[catalog/lineage site](../../site) consumes. It is a full rewrite of the
project's original Windows PowerShell 5.1 pipeline (`src/*.ps1`) with the
same config schema, the same extracted-object file format, and the same
`catalog.json` shape - existing `config/servers.json` files and any
repository already populated by the PowerShell version work with
`syncsql` unmodified.

Unlike the PowerShell version, `syncsql` runs anywhere .NET 10 runs
(Linux, macOS, Windows) and can be installed and run locally, not just
from CI.

**`syncsql` never touches git itself** - no clone, no commit, no push,
and no knowledge of `config.git.*`. It only reads and writes local
files: extracted objects, metrics snapshots, and `catalog.json` (which
its own `catalog build` command optionally reads git history *from*, via
read-only `git log`/`git show`, purely to mine the change heatmap/
co-change/point-in-time features - it never writes to that checkout).
Publishing results to a git repository is
[`scripts/Publish-SyncSqlObjects.ps1`](../../scripts/Publish-SyncSqlObjects.ps1)'s
job (clone, replace the published object tree, commit, push, calling
`syncsql metrics update` and `syncsql catalog build` in between for the
non-git steps) - a plain PowerShell script the CI `sync` stage invokes
with parameters, and that you can run by hand exactly the same way; see
[`.gitlab/README.md`](../../.gitlab/README.md)'s `sync-database-objects`
section. This keeps the tool a pure, git-agnostic data pipeline you can
run and test anywhere, with exactly one place deciding how and where
results get published.

**Nothing about a run comes from the environment.** Every input - the
config path, the credentials for each server, every output path, and
every tuning limit - is a command-line parameter with a sensible local
default, so the same commands work identically on a workstation and in a
pipeline. The only environment fallback left is the pre-existing
`<prefix>_DB_USER`/`<prefix>_DB_PASSWORD` credential pair, kept as the
last-resort layer so an existing CI setup keeps working unchanged.

## Install

`syncsql` is published as a [dotnet global tool](https://learn.microsoft.com/dotnet/core/tools/global-tools).

```bash
dotnet tool install --global SyncSql.Cli --add-source <nexus-nuget-feed-url>
```

Once installed, the `syncsql` command is on your `PATH` (dotnet prints
the exact line to add if it isn't already). Upgrade with:

```bash
dotnet tool update --global SyncSql.Cli --add-source <nexus-nuget-feed-url>
```

Requires the [.NET 10 runtime](https://dotnet.microsoft.com/download) (or
SDK) on the machine running it. No native Oracle client install is
needed - `Oracle.ManagedDataAccess.Core` is a fully managed ADO.NET
driver.

### Building and installing from source

```bash
cd cli
dotnet pack src/SyncSql.Cli -c Release
dotnet tool install --global --add-source ./nupkg SyncSql.Cli
```

#### Build prerequisites

The .NET 10 SDK, and nothing else. No Java, no ANTLR toolchain, no native Oracle client.

`SyncSql.Lineage.Oracle` analyzes Oracle DDL with the full ANTLR4 PL/SQL grammar. Its generated C#
lexer/parser/visitor is committed under [`grammar/`](../../grammar/README.md) and referenced as a
normal project, so restore uses only public NuGet dependencies. Builds, tests, CI, and tool packaging
do not invoke Java, download an ANTLR tool JAR, or require a separately published
`SyncSql.Grammar.PlSql` package.

### Running without installing

Any command below also works as `dotnet run --project cli/src/SyncSql.Cli --`
followed by the same arguments - handy while developing the CLI itself.

## Commands

Every command accepts `-h`/`--help` for the full option list and
`--version` at the root for the tool's version.

### `syncsql validate-config`

Parses and validates a `config/servers.json` file - checks required
fields, filter shapes, and that at least one server is defined. Touches
no database and no git remote.

```bash
syncsql validate-config --config ./config/servers.json
```

| Option     | Default                  | Description                          |
|------------|--------------------------|---------------------------------------|
| `--config` | `./config/servers.json`  | Path to the `config/servers.json` file to validate. |

Exit code `0` on success, `1` with an error message on a validation
failure (missing/invalid field, no servers defined, malformed JSON).

### `syncsql sync`

Extracts every configured (and selected) server: writes each object as
its own `.sql` file and each table's metrics as its own snapshot file.
Purely local - no git operation of any kind. This is the extraction half
of what a scheduled CI pipeline runs; the other half (publishing the
result) is the calling pipeline's job - see [`.gitlab/README.md`](../../.gitlab/README.md).

```bash
syncsql sync --config ./config/servers.json
```

| Option                     | Default                            | Description |
|----------------------------|-------------------------------------|-------------|
| `--config`                 | `./config/servers.json`             | Path to `config/servers.json`. |
| `--output-root`            | `./MSSQL` or `./ORACLE` | Uppercase `servers.type`; explicit override applies to every server. |
| `--staging-root`           | `<output-root>`                     | Local directory each extracted object is written to, as `<server>/<database>/<schema>/<type>/<object>.sql` - the tree starts at the server name, with no wrapping folder. |
| `--metrics-snapshot-root`  | `<output-root>/metrics-snapshot`    | Local directory this run's volatile metrics snapshots are written to (separate from `--staging-root` - one JSON file per table, meant to be folded into history later via `syncsql metrics update`). |
| `--db-user`                | `--credentials-file`, then the environment | Database username for one server, as `PREFIX=value` (`PREFIX` = that server's `credentialsVariablePrefix`). Repeatable. |
| `--db-password`            | `--credentials-file`, then the environment | Database password for one server, as `PREFIX=value`. Repeatable. Only the first `=` separates, so a password containing `=` needs no escaping. |
| `--credentials-file`       | *(none)*                            | JSON file of credentials keyed by `credentialsVariablePrefix` - see [Credentials](#credentials). |
| `--server-include`         | `config.serverSelection.include`    | Regex a server name must match to run. Repeatable. Overrides the config value entirely when passed. |
| `--server-exclude`         | `config.serverSelection.exclude`    | Regex that excludes a server. Repeatable. Overrides the config value entirely when passed. |
| `--max-parallelism`        | `4`                                | Maximum concurrent server extractions, including writing their objects and snapshots. Must be positive; use `1` for sequential extraction. |

Each server uses its own database connections. Queries within a server remain
sequential. Linked-server discovery runs in depth rounds with the same concurrency
limit; duplicate targets are resolved in configuration order, so the chosen parent
and inherited credentials do not depend on which extraction finishes first.

In an interactive terminal, extraction displays an animated overall progress bar
and one row per server: queued, extracting, writing, done, failed, skipped, or cancelled.
Rows show elapsed time, extracted object counts, the current database/schema or
object, and completed/total counts when known (including files being written).
Overall percentage measures servers finished, not estimated query time. Discovery
can increase the total while the run is in progress. Large fleets rotate through
pages every three seconds to fit the terminal; the final summary lists every server.
Warnings and errors stay visible above the display. Redirected output and `TERM=dumb`
use plain log lines without animation or terminal escape sequences.

Exit code `0` if every selected server extracted successfully; `1` if
any server failed (extraction error or missing credentials). A partial
failure does not stop the run - other servers still extract. A server
reached by following a linked server (see `discovery.linkedServers` below)
is the exception: failing to extract one is logged as a warning and does
not fail the run, since it's a lead this run chose to chase rather than
part of the configured job.

`--server-include`/`--server-exclude` make it possible to fan extraction
out across a fleet as independent parallel jobs, each scoped to one
server, all writing into the same `--staging-root`/`--metrics-snapshot-root`
- safe, since extraction always writes under `<server>/...` first, so
different servers never collide. See [`.gitlab/README.md`](../../.gitlab/README.md)'s
`extract-server` section (a GitLab `parallel: matrix:` over server names)
for the reference setup.

### `syncsql catalog build`

Standalone `catalog.json` builder: walks an already-extracted tree and
(re)builds the catalog. Mirrors the original `Build-Catalog.ps1`. Useful
for local preview, or for a CI pipeline to call after it has cloned a
target repository and populated `--repo-root`/`--path-prefix` itself.

```bash
syncsql catalog build --objects-root ./staging --output ./catalog.json
```

| Option                          | Default   | Description |
|----------------------------------|-----------|-------------|
| `--output-root`                  | Existing `./MSSQL` and `./ORACLE` | Process each engine root separately; override to select one root. |
| `--objects-root`                 | `<output-root>` | Root of the extracted tree (`server/database/[schema/]type/object.sql`) - i.e. what `syncsql sync` just wrote. |
| `--output`                       | `<output-root>/catalog.json` | File path the catalog JSON is written to. |
| `--repo-root`                    | *(none)*  | Git checkout containing `--path-prefix`, mined **read-only** (`git log`/`git show`) for history/heatmap/point-in-time data - never written to. Omit to skip all of that (empty history, zero change counts) rather than failing. |
| `--path-prefix`                  | *(empty)* | Folder inside `--repo-root` holding the extracted tree. Empty (the default) means the tree starts at the repository root, so the first path segment is the server name. |
| `--history-limit`                | `250`     | Maximum number of commits (touching `--path-prefix`) to mine. |
| `--max-versions-per-object`      | `15`      | Maximum historical versions kept (and content-fetched via `git show`) per object, most recent first. |
| `--max-history-content-calls`    | `1500`    | Hard cap on total `git show` invocations across the whole mining pass, so a large/old repo can't turn this into an unbounded job. |
| `--max-co-change-commit-size`    | `40`      | Commits touching more files than this are excluded from co-change pair counting (almost always a bulk/initial sync, not a meaningful signal). |
| `--metrics-root`                 | *(none)*  | Root of the accumulating metrics history tree (`metrics update`'s `--history-root`, e.g. `<output-root>/metrics`). Omit to skip - `node.metrics` is left empty. |

Lineage edges are inferred with a real parser per engine - `Microsoft.SqlServer.TransactSql.ScriptDom`
for MSSQL objects, a vendored ANTLR PL/SQL grammar for Oracle objects -
not text/regex matching, so string literals, comments, and quoted
identifiers are never mistaken for object references. That also covers the
two MSSQL object families whose data flow is asynchronous: a replication
publication's `sp_addarticle` declarations become edges to the tables it
publishes, and Service Broker `SEND`/`RECEIVE`/`BEGIN DIALOG`/queue
activation become edges to the message type, queue, contract or procedure
they name. A `BEGIN DIALOG` targeting another instance's Broker GUID (or a
variable) is left unresolved rather than bound to a same-named local
service.

Every reference that resolves nowhere the lookup reaches - the object's own
database, the other databases on its server, then the servers one linked
server / database link away - is collected as an **orphaned reference** and
written to `catalog.json`'s `orphanedReferences` array
(`from`/`server`/`database`/`schema`/`name`) instead of silently dropped,
with a summary count logged as a warning. Several cases are deliberately not
flagged, because none of them means the target is missing: a merely
*ambiguous* reference (more than one same-named object in scope - see
`NodeIndex.Resolve` in `SyncSql.Catalog`), a system object the engine
provides, a temp table / CTE / statement alias the script creates for itself,
a target outside what is extracted (recorded against the link instead), and a
reference recovered from dynamically-built SQL. The
[root README](../../README.md#orphaned-reference-detection) documents the
full rule.

Exit code `0` on success, `1` if `--objects-root` doesn't exist.

### `syncsql metrics update`

Folds this run's freshly captured metrics snapshots (row counts, index
fragmentation/usage, optimizer statistics) into a growing per-object
history array, kept entirely separate from each object's own versioned
`.sql` file. Mirrors the original `Update-MetricsHistory.ps1`. Purely
local - reads `--snapshot-root`, writes `--history-root`, no git
operation of any kind.

```bash
syncsql metrics update --snapshot-root ./metrics-snapshot --history-root ./metrics
```

| Option              | Default | Description |
|----------------------|---------|-------------|
| `--output-root`      | Existing `./MSSQL` and `./ORACLE` | Process each engine root separately; override to select one root. |
| `--snapshot-root`    | `<output-root>/metrics-snapshot` | Root of this run's freshly captured snapshot tree (one JSON file per object, same relative path/id as the object's own `.sql` file - `sync`'s `--metrics-snapshot-root`). |
| `--history-root`     | `<output-root>/metrics` | Root of the accumulating history tree; in a pipeline, `<target-repo-checkout>/metrics`. Kept outside the object tree so a wipe-and-replace of it never touches the history. |
| `--history-limit`    | `90`    | Maximum snapshots retained per object; oldest are trimmed first. |

### `syncsql lint`

Lints T-SQL script(s) with the same real parser (`Microsoft.SqlServer.TransactSql.ScriptDom`)
`catalog build` uses for lineage - not regex/text matching. Reports:

- actual **syntax errors** from the parser (rule `syntax-error`, default `error` severity), and
- a small set of style/best-practice findings, run over the parsed AST:

  | Rule            | What it flags |
  |------------------|----------------|
  | `select-star`    | `SELECT *` / `SELECT alias.*` - an added/dropped/reordered column silently changes what callers get back. |
  | `nolock-hint`    | `WITH (NOLOCK)` / `READUNCOMMITTED` table hints - allows dirty reads; often copy-pasted as a perf fix rather than a deliberate isolation-level choice. |
  | `cursor-usage`   | `DECLARE ... CURSOR` - row-by-row processing that's usually much slower than an equivalent set-based rewrite. |

Touches no database - it only reads files off disk, so it works equally well
against a fresh `syncsql sync` staging tree or a hand-written `.sql` file
before it's ever run against a server.

```bash
syncsql lint --path ./staging
```

| Option          | Default   | Description |
|-----------------|-----------|-------------|
| `--output-root` | `./MSSQL` | Default T-SQL input directory; Oracle exports are excluded. |
| `--path`        | `<output-root>` | A `.sql` file, or a directory searched recursively for `*.sql` files. Repeatable. |
| `--config` | `./config/sql-style.json`, or packaged defaults if absent | Shared SQL lint/format JSON. See [`config/README.md`](../../config/README.md). |
| `--fail-on`  | `lint.failOn` in the JSON (`error` by default) | Overrides the minimum finding severity that makes the command exit non-zero: `warning` or `error`. |

Findings are logged one per line as `path:line:column [rule-id] message`, at
`ERROR` or `WARN` level depending on severity, followed by a summary line.
Exit code `0` if nothing at or above `--fail-on` was found; `1` otherwise (or
if a given `--path` doesn't exist).

## Configuration

`syncsql` reads the exact same `config/servers.json` schema as the
PowerShell pipeline - copy `config/servers.example.json` to
`config/servers.json` and edit it. Nothing in that file is secret: it
lists server hostnames and the regex filters that decide what gets
extracted.

- **`git`**: where extracted objects get pushed - `remoteUrl`, `branch`
  (default `main`), `pathPrefix` (default empty - the extracted tree starts
  at the repository root), `commitUserName`,
  `commitUserEmail`, `commitMessage`. `syncsql` itself never reads or
  acts on this block - it exists purely as part of the config schema
  `validate-config` checks. [`scripts/Publish-SyncSqlObjects.ps1`](../../scripts/Publish-SyncSqlObjects.ps1)
  is what resolves and acts on it, and each field there is also a script
  parameter that overrides the config value (see
  [`.gitlab/README.md`](../../.gitlab/README.md)); the CI job passes
  `-RemoteUrl` built from the GitLab predefined variables
  `CI_SERVER_PROTOCOL`/`CI_SERVER_HOST`/`CI_PROJECT_PATH` when `remoteUrl`
  is left blank, so objects go back into the same project.
- **`defaults`** / per-server overrides: `databases`, `schemas`,
  `objectNames` include/exclude regex lists, and an `objectTypes` list
  (`Schemas`, `Types`, `Tables`, `Views`, `StoredProcedures`, `Functions`,
  `Triggers`, `Synonyms`, `LinkedServers`, `Replication`, and the four
  Service Broker types `MessageTypes`, `Contracts`, `Queues`, `Services`
  for MSSQL; `Schemas`, `Types`, `TypeBodies`, `Tables`, `Views`,
  `Procedures`, `Functions`, `Packages`, `PackageBodies`, `Triggers`,
  `Synonyms`, `DatabaseLinks` for Oracle).
  A server that specifies a key fully replaces the default for that key
  - it does not merge with it. The Service Broker types are opt-in and are
  not in `config/servers.example.json`; naming any one of them makes the
  extractor read the database's Broker catalog for that type, skipping the
  engine-provided definitions.
- **`serverSelection`**: regex filter over which of the listed servers
  actually run in a given invocation (`sync --server-include`/`--server-exclude`
  override this per run).
- **`discovery.linkedServers`**: opt-in follow-up of the linked servers a
  run finds, so `sync` also extracts servers nobody listed by hand:

  ```json
  "discovery": {
    "linkedServers": {
      "enabled": true,
      "maxDepth": 1,
      "requireMatchingLogin": true,
      "restrictToLinkedCatalog": true,
      "linkNames": { "include": [".*"], "exclude": [] }
    }
  }
  ```

  A followed link becomes a server entry that inherits everything from the
  one that declared it - port, TLS settings, schema/objectName/objectType
  filters, and its `credentialsVariablePrefix`, so the *same username and
  password* are used on the far side. `requireMatchingLogin` (default true)
  keeps that honest by only following a link whose remote login is that same
  username or that passes the local login through;
  `restrictToLinkedCatalog` (default true) limits the follow-up to the
  database the link pins. `maxDepth` bounds how many links deep the run
  goes (0 disables it). Non-SQL-Server links, links without a data source,
  and links to a host a configured server already covers are skipped with a
  logged reason; Oracle database links are not followed. Discovered servers
  are named after the link, which is also their output path segment. See the
  main [README](../../README.md#following-linked-servers).

Filtering is regex-based (.NET regex syntax) and works at every level:
server, database, schema, and individual object name. An exclude match
always wins over an include match; an empty/missing include list means
"include everything."

## Output layout

`sync` defaults to the uppercase `servers.type` in the current directory:
`./MSSQL` or `./ORACLE`. Mixed-engine runs use both roots. `catalog build`
and `metrics update` visit each existing engine root by default; T-SQL
`lint` uses `./MSSQL`. Pass `--output-root` to select a single custom root.
Each engine folder has this layout:

```
./MSSQL/              # or ./ORACLE/
├── <server>/          # sync --staging-root         → catalog build --objects-root, lint --path
│   └── <database>/<schema>/<type>/<object>.sql
├── metrics-snapshot/  # sync --metrics-snapshot-root → metrics update --snapshot-root
├── metrics/           # metrics update --history-root → catalog build --metrics-root
└── catalog.json       # catalog build --output
```

The extracted objects are the output root's own contents rather than a folder
inside it, so the first path segment is always the server they came from. The
two sibling folders hold JSON only, so the `*.sql` scans that `catalog build`
and `lint` do over the root never pick them up.

When both engine roots exist, select one with `--output-root` before supplying a single catalog `--output` or metrics `--history-root`. An explicit `--objects-root` locates the default catalog beside that input; an explicit `--snapshot-root` locates default history beside the snapshot directory.

Any single path can still be pinned explicitly, and the CI pipeline pins
all of them (see [`.gitlab/README.md`](../../.gitlab/README.md)). Relative
paths resolve against the current directory; the resolved absolute path is
what gets logged.

## Credentials

Credentials are **never** stored in the config. Each server entry has a
`credentialsVariablePrefix`, and `syncsql sync` resolves that prefix
against three sources, in order - each half (username, password)
independently, so a username passed as a parameter can be completed by a
password that only the environment has:

1. **`--db-user PREFIX=value` / `--db-password PREFIX=value`** - repeatable
   parameters, one pair per server. This is how a CI job passes credentials
   in without the CLI needing anything in its environment.
2. **`--credentials-file <path>`** - a JSON file keyed by prefix:

   ```json
   {
     "SQLPROD01": { "user": "svc_syncsql", "password": "..." },
     "ORAPROD01": { "user": "SYNCSQL", "password": "..." }
   }
   ```

   Either half may be omitted and filled in by a lower layer. Keep the file
   outside the repository and readable only by the account running `syncsql`.
3. **`<prefix>_DB_USER` / `<prefix>_DB_PASSWORD` environment variables** -
   the original behaviour, kept as the last-resort layer so an existing
   setup keeps working with no parameter changes.

A server whose username or password can't be found in any of the three is
skipped (logged as an error naming every source tried, counted as a
failure) rather than aborting the whole run.

Command-line arguments are visible to other processes on the same host, so
on a shared machine prefer `--credentials-file` (or the environment) over
`--db-password` for the password half.

The git push token used when publishing never passes through `syncsql` at
all: [`scripts/Publish-SyncSqlObjects.ps1`](../../scripts/Publish-SyncSqlObjects.ps1)
hands it to `git` through a credential helper that reads it from that
script's own environment - never a command-line argument, never written to
disk, and never embedded in the remote URL, so it cannot leak through a
process listing, `git remote -v`, or shell history.

## Exit codes

| Code | Meaning |
|------|---------|
| `0`  | Success. |
| `1`  | A handled failure: invalid config, a missing `--objects-root`, or one or more servers failed to extract or had missing credentials. |
| other | An unhandled exception - treat as a bug; the exception message and stack trace are printed. |

## Running locally

Nothing here needs a CI job, an exported variable, or a path parameter -
the whole flow runs from a checkout with credentials in a file:

```bash
cat > ~/.syncsql-credentials.json <<'JSON'
{ "SQLPROD01": { "user": "svc_syncsql", "password": "..." } }
JSON
chmod 600 ~/.syncsql-credentials.json

syncsql sync --credentials-file ~/.syncsql-credentials.json   # → ./MSSQL/ and/or ./ORACLE/
syncsql metrics update                                        # → each engine's metrics/
syncsql catalog build                                         # → each engine's catalog.json
syncsql catalog build --output-root ./MSSQL --metrics-root ./MSSQL/metrics # include MSSQL metrics
```

`--config` defaults to `./config/servers.json`, so a checkout with a real
config needs no parameter for it either. Pass credentials as `--db-user
SQLPROD01=svc_syncsql --db-password SQLPROD01=...` instead of the file if
you prefer, or leave the `SQLPROD01_DB_USER`/`SQLPROD01_DB_PASSWORD`
variables exported in your shell - all three work.

To preview the site against that catalog, point `catalog build --output` at
it and run `npm run dev` inside `site/`:

```bash
syncsql catalog build --output-root ./MSSQL --output ./site/public/data/catalog.json
```

Add `--repo-root`/`--path-prefix` pointed at a real git checkout of your
target repository to include history/heatmap/co-change data (read-only -
nothing is written back to that checkout), and `--metrics-root
<metrics-history-dir>` to include accumulated metrics trends (a single
local run only ever has one snapshot to show - real trend graphs need
several runs' worth of history accumulated in a real `metrics/` tree,
via `syncsql metrics update`).

Publishing the result into a git repository - the same steps CI runs - is
one script away, and `-SkipPush` makes it a dry run:

```bash
pwsh ./scripts/Publish-SyncSqlObjects.ps1 \
  -ExtractedObjectsDir ./MSSQL \
  -MetricsSnapshotDir ./MSSQL/metrics-snapshot \
  -ConfigPath ./config/servers.json \
  -SkipPush
```

The script targets **Windows PowerShell 5.1**, so `powershell.exe` runs it
on a stock Windows box with nothing installed, and `pwsh` (PowerShell 7+)
runs the same file on Linux/macOS. See
[`.gitlab/README.md`](../../.gitlab/README.md)'s `sync-database-objects`
section for its full parameter list.

## Architecture

`syncsql` is built as a small Clean/Onion-architecture solution under
`cli/`:

- **`SyncSql.Core`** - domain records and interfaces only; no database
  driver, parser, or git dependency. `SyncSql.Core.Credentials` holds the
  credential sources (parameters, credentials file, environment) and the
  layered provider that stacks them.
- **`SyncSql.Extraction.MsSql`** / **`SyncSql.Extraction.Oracle`** -
  one `IDatabaseObjectExtractor` per engine (`Microsoft.Data.SqlClient`,
  `Oracle.ManagedDataAccess.Core`).
- **`SyncSql.Lineage.MsSql`** / **`SyncSql.Lineage.Oracle`** - one
  `ILineageAnalyzer` per engine (`Microsoft.SqlServer.TransactSql.ScriptDom`,
  a vendored ANTLR PL/SQL grammar).
- **`SyncSql.Catalog`** - node assembly, per-engine lineage dispatch,
  read-only git history mining (`git log`/`git show`, via `IProcessRunner`),
  metrics folding.
- **`SyncSql.Cli`** - the composition root: wires every implementation
  above via keyed dependency injection and exposes the commands
  documented here.

Every project outside `SyncSql.Cli` depends only inward on `SyncSql.Core`
- adding a third database engine is a new extraction/lineage project
pair plus one DI registration, with no change to `SyncSql.Catalog` or
the CLI's orchestration logic. There is deliberately no `SyncSql.Git`
project or any git-write abstraction anywhere in this solution - see the
note at the top of this document.
