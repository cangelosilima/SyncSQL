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
Publishing results to a git repository is entirely the calling
pipeline's responsibility - see the root `.gitlab-ci.yml`'s
`sync-database-objects` job for the reference implementation (clone,
replace `config.git.pathPrefix`, commit, push, calling `syncsql metrics
update` and `syncsql catalog build` in between for the non-git steps).
This keeps the tool a pure, git-agnostic data pipeline you can run and
test anywhere, with exactly one place deciding how and where results get
published.

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

| Option     | Required | Description                          |
|------------|----------|---------------------------------------|
| `--config` | yes      | Path to the `config/servers.json` file to validate. |

Exit code `0` on success, `1` with an error message on a validation
failure (missing/invalid field, no servers defined, malformed JSON).

### `syncsql sync`

Extracts every configured (and selected) server: writes each object as
its own `.sql` file and each table's metrics as its own snapshot file.
Purely local - no git operation of any kind. This is the extraction half
of what a scheduled CI pipeline runs; the other half (publishing the
result) is the calling pipeline's job - see the root `.gitlab-ci.yml`.

```bash
syncsql sync --config ./config/servers.json
```

| Option                     | Default                          | Description |
|----------------------------|-----------------------------------|-------------|
| `--config`                 | *(required)*                      | Path to `config/servers.json`. |
| `--staging-root`           | a fresh temp directory            | Local directory each extracted object is written to. |
| `--metrics-snapshot-root`  | a fresh temp directory            | Local directory this run's volatile metrics snapshots are written to (separate from `--staging-root` - one JSON file per table, meant to be folded into history later via `syncsql metrics update`). |
| `--server-include`         | `config.serverSelection.include`  | Regex a server name must match to run. Repeatable. Overrides the config value entirely when passed. |
| `--server-exclude`         | `config.serverSelection.exclude`  | Regex that excludes a server. Repeatable. Overrides the config value entirely when passed. |

Exit code `0` if every selected server extracted successfully; `1` if
any server failed (extraction error or missing credentials). A partial
failure does not stop the run - other servers still extract.

`--server-include`/`--server-exclude` make it possible to fan extraction
out across a fleet as independent parallel jobs, each scoped to one
server, all writing into the same `--staging-root`/`--metrics-snapshot-root`
- safe, since extraction always writes under `<server>/...` first, so
different servers never collide. See the root `.gitlab-ci.yml`'s
`extract-server` job (a GitLab `parallel: matrix:` over server names)
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
| `--objects-root`                 | *(required)* | Root of the extracted tree (`server/database/type/[schema/]object.sql`). |
| `--output`                       | *(required)* | File path the catalog JSON is written to. |
| `--repo-root`                    | *(none)*  | Git checkout containing `--path-prefix`, mined **read-only** (`git log`/`git show`) for history/heatmap/point-in-time data - never written to. Omit to skip all of that (empty history, zero change counts) rather than failing. |
| `--path-prefix`                  | `objects` | Folder inside `--repo-root` holding the extracted tree. |
| `--history-limit`                | `250`     | Maximum number of commits (touching `--path-prefix`) to mine. |
| `--max-versions-per-object`      | `15`      | Maximum historical versions kept (and content-fetched via `git show`) per object, most recent first. |
| `--max-history-content-calls`    | `1500`    | Hard cap on total `git show` invocations across the whole mining pass, so a large/old repo can't turn this into an unbounded job. |
| `--max-co-change-commit-size`    | `40`      | Commits touching more files than this are excluded from co-change pair counting (almost always a bulk/initial sync, not a meaningful signal). |
| `--metrics-root`                 | *(none)*  | Root of the accumulating metrics history tree (`metrics update`'s `--history-root`). Omit to skip - `node.metrics` is left empty. |

Lineage edges are inferred with a real parser per engine - `Microsoft.SqlServer.TransactSql.ScriptDom`
for MSSQL objects, a vendored ANTLR PL/SQL grammar for Oracle objects -
not text/regex matching, so string literals, comments, and quoted
identifiers are never mistaken for object references.

Every reference that resolves nowhere in scope (same server+database, or
bare on the same server) - typically because the target was renamed or
dropped and this object's DDL was never updated - is collected as an
**orphaned reference** and written to `catalog.json`'s `orphanedReferences`
array (`from`/`schema`/`name`) instead of silently dropped, with a summary
count logged as a warning. A reference that's merely *ambiguous* (more than
one same-named object in scope) is not flagged this way - see
`NodeIndex.Resolve` in `SyncSql.Catalog` for the distinction.

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
| `--snapshot-root`    | *(required)* | Root of this run's freshly captured snapshot tree (one JSON file per object, same relative path/id as the object's own `.sql` file - `sync`'s `--metrics-snapshot-root`). |
| `--history-root`     | *(required)* | Root of the accumulating history tree, e.g. `<target-repo-checkout>/metrics`. Kept outside `config.git.pathPrefix` so a wipe-and-replace of the object tree never touches it. |
| `--history-limit`    | `90`    | Maximum snapshots retained per object; oldest are trimmed first. |

## Configuration

`syncsql` reads the exact same `config/servers.json` schema as the
PowerShell pipeline - copy `config/servers.example.json` to
`config/servers.json` and edit it. Nothing in that file is secret: it
lists server hostnames and the regex filters that decide what gets
extracted.

- **`git`**: where extracted objects get pushed - `remoteUrl`, `branch`
  (default `main`), `pathPrefix` (default `objects`), `commitUserName`,
  `commitUserEmail`, `commitMessage`. `syncsql` itself never reads or
  acts on this block - it exists purely as part of the config schema
  `validate-config` checks. The calling pipeline resolves and acts on it
  directly (see the root `.gitlab-ci.yml`, which reads it via `jq`);
  leaving `remoteUrl` blank there means push back into the repository
  identified by the GitLab CI predefined variables
  `CI_SERVER_PROTOCOL`/`CI_SERVER_HOST`/`CI_PROJECT_PATH`.
- **`defaults`** / per-server overrides: `databases`, `schemas`,
  `objectNames` include/exclude regex lists, and an `objectTypes` list
  (`Schemas`, `Tables`, `Views`, `StoredProcedures`, `Functions`,
  `Triggers`, `Synonyms`, `LinkedServers`, `Replication` for MSSQL;
  `Schemas`, `Tables`, `Views`, `Procedures`, `Functions`, `Packages`,
  `PackageBodies`, `Triggers`, `Synonyms`, `DatabaseLinks` for Oracle).
  A server that specifies a key fully replaces the default for that key
  - it does not merge with it.
- **`serverSelection`**: regex filter over which of the listed servers
  actually run in a given invocation (`sync --server-include`/`--server-exclude`
  override this per run).

Filtering is regex-based (.NET regex syntax) and works at every level:
server, database, schema, and individual object name. An exclude match
always wins over an include match; an empty/missing include list means
"include everything."

## Credentials

Credentials are **never** stored in the config. Each server entry has a
`credentialsVariablePrefix`; `syncsql` reads
`<prefix>_DB_USER` / `<prefix>_DB_PASSWORD` from the process
environment. A server missing either variable is skipped (logged as an
error, counted as a failure) rather than aborting the whole run.

The git push token used by the calling pipeline (`CI_JOB_Maintainer_Token`,
in the reference `.gitlab-ci.yml`) never passes through `syncsql` at
all - the pipeline hands it to `git` directly via `GIT_ASKPASS` plus a
process environment variable, never a command-line argument and never
embedded in the remote URL, so it cannot leak through a process listing,
`git remote -v`, or shell history.

## Exit codes

| Code | Meaning |
|------|---------|
| `0`  | Success. |
| `1`  | A handled failure: invalid config, a missing `--objects-root`, or one or more servers failed to extract or had missing credentials. |
| other | An unhandled exception - treat as a bug; the exception message and stack trace are printed. |

## Running locally

```bash
syncsql sync --config ./config/servers.json --staging-root ./staging
```

Leaves the extracted files under `./staging`. To build a `catalog.json`
for local preview from that staging directory:

```bash
syncsql catalog build \
  --objects-root ./staging \
  --output ./site/public/data/catalog.json
```

Add `--repo-root`/`--path-prefix` pointed at a real git checkout of your
target repository to include history/heatmap/co-change data (read-only -
nothing is written back to that checkout), and `--metrics-root
<metrics-history-dir>` to include accumulated metrics trends (a single
local run only ever has one snapshot to show - real trend graphs need
several runs' worth of history accumulated in a real `metrics/` tree,
via `syncsql metrics update`). Then `npm run dev` inside `site/`.

## Architecture

`syncsql` is built as a small Clean/Onion-architecture solution under
`cli/`:

- **`SyncSql.Core`** - domain records and interfaces only; no database
  driver, parser, or git dependency.
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
