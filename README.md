# SyncSQL

Scheduled extraction of database objects (stored procedures, views,
functions, triggers, tables — with foreign keys, check constraints,
indexes and statistics attached — schemas, synonyms, linked servers /
database links, and best-effort replication topology) from a fleet of
**MSSQL** and **Oracle** servers into this repository — one file per
object, one commit per run, diffable like any other source code —
followed by a structure/lineage/history analysis published as a browsable
**React site on GitLab Pages**.

Built as a GitLab CI scheduled pipeline driven entirely by PowerShell
(pwsh) for the extraction itself, so the same script runs unmodified on any
runner that can pull the `mcr.microsoft.com/powershell` image, with no
OS-specific tooling and no native Oracle client install required.

## How it works

```
config/servers.yml  --defines-->  which servers/databases/schemas/objects
        |
        v
Bootstrap-Dependencies.ps1   (SqlServer + powershell-yaml modules,
                               Oracle.ManagedDataAccess.Core driver)
        |
        v
Export-DatabaseObjects.ps1                              [CI stage: sync]
        |
        +--> SyncSql.MsSql.psm1    (Invoke-Sqlcmd against sys.sql_modules,
        |                           sys.tables + FKs/checks/indexes/stats,
        |                           sys.servers, sys.synonyms, replication
        |                           publications, optionally
        |                           sys.extended_properties)
        +--> SyncSql.Oracle.psm1   (DBMS_METADATA.GET_DDL via the managed
        |                           ADO.NET driver, no Instant Client)
        |
        v
extracted-objects/<server>/<database>/<objectType>/[<schema>/]<object>.sql
        |
        +-----------------------------------------------+
        |                                                |
        v                                                v
SyncSql.Git.psm1                                Build-Catalog.ps1   [CI stage: analyze]
  clone this project, replace <pathPrefix>/        walks the extracted tree, infers
  with the staged tree (dropped objects show        best-effort lineage edges, mines
  up as deletions), commit, push                     this project's own git history for
                                                       change frequency / co-change /
                                                       per-object versions, writes
                                                       catalog/catalog.json
                                                                |
                                                                v
                                                        site/ (React + Vite)   [CI stage: pages]
                                                          npm run build with catalog.json
                                                          copied into public/data/,
                                                          published as a GitLab Pages site
```

Every extracted `.sql` file gets a small static header (server / database /
type / object name) and nothing else — no timestamps — so re-running the
pipeline with no underlying database changes produces **zero diff**.

## Configuration

Copy `config/servers.example.yml` to `config/servers.yml` and edit it.
Nothing in that file is secret: it lists server hostnames and the regex
filters that decide what gets extracted. See the comments in the example
file for the full schema; in short:

- `git`: where extracted objects get pushed. Left blank (the default),
  they're pushed back into **this same project** using the predefined
  `CI_SERVER_*` variables — see the token requirement below. Set it to a
  full URL to push into a different project instead.
- `defaults` / per-server overrides: `databases`, `schemas`,
  `objectNames` include/exclude regex lists, and an `objectTypes` list
  (`Schemas`, `Tables`, `Views`, `StoredProcedures`, `Functions`,
  `Triggers`, `Synonyms`, `LinkedServers`, `Replication` for MSSQL;
  `Schemas`, `Tables`, `Views`, `Procedures`, `Functions`, `Packages`,
  `PackageBodies`, `Triggers`, `Synonyms`, `DatabaseLinks` for Oracle).
  A server that specifies a key fully replaces the default for that key.
- `serverSelection`: regex filter over which of the listed servers
  actually run in a given pipeline execution (can also be overridden per
  run with `-ServerNameInclude` / `-ServerNameExclude`).

Filtering is regex-based and works at every level mentioned in the
config: server, database, schema, and individual object name.

Credentials are **never** stored in the config. Each server entry has a
`credentialsVariablePrefix`; the pipeline reads
`<prefix>_DB_USER` / `<prefix>_DB_PASSWORD` from the environment.

## Required CI/CD variables

Set these under **Settings > CI/CD > Variables** (masked + protected):

| Variable                            | Purpose                                                                                                    |
|--------------------------------------|-------------------------------------------------------------------------------------------------------------|
| `CI_JOB_Maintainer_Token`            | A project access token with the **Maintainer** role and `write_repository` scope, used to push extracted objects back into this project. The built-in `CI_JOB_TOKEN` cannot push commits, hence a dedicated token. |
| `<PREFIX>_DB_USER` / `_DB_PASSWORD`  | One pair per server entry in `config/servers.yml`                                                          |

This is only needed if `git.remoteUrl` is left blank (the default,
self-repo target). If you point `git.remoteUrl` at a different project,
`CI_JOB_Maintainer_Token` needs Maintainer/`write_repository` access there
instead.

Optional: `HISTORY_LIMIT` (default `250`) controls how many commits the
`analyze` stage mines for the heatmap / co-change / point-in-time features
— see "History, heatmap and point-in-time" below.

## Running the pipeline

`.gitlab-ci.yml` defines four jobs across four stages:

- **validate-config** (`validate`): runs on merge requests / pushes, just
  checks that `config/servers.yml` (or the example file, if you haven't
  added one yet) parses and satisfies the schema. No database or git
  credentials needed.
- **sync-database-objects** (`sync`): the actual extraction, and the only
  job that touches your databases and pushes to git. Produces the
  `extracted-objects/` artifact consumed by the next stage, plus a
  `dotenv` report (`PATH_PREFIX`/`GIT_BRANCH`) so the analyze stage always
  mines the same path/branch this run just pushed to.
- **analyze-catalog** (`analyze`): fetches the branch tip (to see the
  commit sync-database-objects may have just pushed), then runs
  `Build-Catalog.ps1` over `extracted-objects/` to produce
  `catalog/catalog.json` (structure, best-effort lineage graph, and mined
  git history).
- **pages** (`pages`): builds `site/` (React/Vite) with that `catalog.json`
  and publishes it as this project's GitLab Pages site.

The last three only run for `schedule` (and manually-triggered `web`/API)
pipeline sources — create a schedule under **CI/CD > Schedules** pointing
at this project. Once it's run once, find the site URL under **Settings >
Pages**.

## The catalog / lineage site

`site/` is a React + TypeScript + Vite app (source checked into this repo,
built fresh by the `pages` job on every scheduled run), styled as a dense,
dark data-terminal:

- **Overview** — object counts, the 10 most recently changed objects, the
  most-referenced tables (direct incoming edges and indirect/transitive
  reachability, capped to one hop across a linked-server boundary), a
  change-frequency heatmap, and objects that tend to change together in
  the same commit.
- **Explorer** — a sortable, filterable table listing every object. The
  server/database tree from earlier versions is now a toggleable drawer
  (hamburger icon, top left) rather than an always-open sidebar; Explorer
  is the primary way to browse.
- **Object detail** — qualified name, `sys.extended_properties`
  descriptions (object + column level, MSSQL only), the DDL, structured
  panels for any Foreign Keys / Check Constraints / Indexes / Statistics
  sections, "depends on" / "used by" lineage lists with an embedded
  neighborhood graph, and a change-history list with a point-in-time
  viewer (see below).
- **Lineage** — a full graph explorer (`/#/lineage`), filterable, rendered
  with `@xyflow/react` + `dagre` auto-layout.
- **History** — a global commit timeline of everything the pipeline has
  changed, expandable per commit.

All of Explorer/Lineage/the sidebar's tree search share one GitLab-style
filter bar: type to get attribute suggestions (server, database, schema,
type, name, description), pick an operator (is / is not / contains / is
in / is not in), then pick from suggested values pulled from the catalog.
Suggestion lookups are capped and debounced, and committed filters (not
keystrokes) are what actually re-filter the object list, so it stays
responsive on large catalogs.

The tree (in the drawer, and Explorer's implicit grouping) is
Server → Database → Schema → Type → Object; schema-less types (Schemas,
LinkedServers, Replication, DatabaseLinks) land under a synthetic
"(server-level)" bucket.

**Lineage is inferred, not parsed.** `Build-Catalog.ps1` regex-matches
identifiers found in each object's DDL text (plus its Foreign Keys section,
which is structural rather than inferred) against every other known object
name. It is a reasonable starting point for exploration, not a certified
lineage report — it will miss dynamic SQL, and can occasionally produce a
false-positive edge when an identifier collides with an unrelated object
name. Any traversal that crosses a linked-server/DB-link boundary (in the
"most referenced indirectly" analytics) stops one hop past that boundary
rather than fanning out across a remote server's own dependency graph. The
site says as much on its overview page.

### History, heatmap and point-in-time

A static Pages site can't run live `git` queries, so `Build-Catalog.ps1`
mines history *during the analyze CI stage* instead, when it has an actual
git checkout of this project to work with (`-RepoRoot`, bounded to
`-HistoryLimit` commits, default 250 — override via the `HISTORY_LIMIT` CI
variable). That produces:

- a global commit timeline (the History page and Overview's "latest
  changes"),
- per-object change counts / last-changed dates (Explorer columns, the
  heatmap),
- co-change pairs — objects that keep showing up in the same commit,
- and a bounded per-object version history with DDL content fetched via
  `git show`, powering the "view this object as of a past commit" selector
  on the object detail page.

This is **not** a full whole-database time machine — reconstructing the
entire catalog (including lineage) at every historical commit would mean
re-running the whole analysis per commit, which doesn't fit a scheduled
CI job. What you get is real historical DDL per object within the mined
commit window, plus a commit-level view of what changed together, which
covers the practical "what changed and when" questions without that cost.
Running without `-RepoRoot` (e.g. local `-SkipGit` testing) simply omits
all of this — empty history, zero change counts — rather than failing.

To work on the site locally:

```sh
cd site
npm install
npm run dev
```

`site/public/data/catalog.json` ships a small demo fixture so `npm run dev`
has something to render before any pipeline has actually run; the `pages`
CI job overwrites it with the real, freshly generated catalog on every
build.

## Running the extraction locally

```pwsh
pwsh ./src/Bootstrap-Dependencies.ps1
$env:SQLPROD01_DB_USER = '...'
$env:SQLPROD01_DB_PASSWORD = '...'
pwsh ./src/Export-DatabaseObjects.ps1 -ConfigPath ./config/servers.yml -SkipGit
```

`-SkipGit` leaves the extracted files under a temp staging directory
(printed in the log) instead of publishing them. Feed that directory into
`Build-Catalog.ps1 -ObjectsRoot <dir> -OutputPath ./site/public/data/catalog.json`
(add `-RepoRoot`/`-PathPrefix` pointed at a real git checkout to include
history) to preview the real site locally with `npm run dev` inside `site/`.

## Known limitations (v2)

- MSSQL table DDL (columns, identity, defaults, primary key) is
  reconstructed from catalog views since SQL Server doesn't store table
  definitions as text the way it does for procedures/views. Foreign keys,
  check constraints, non-PK indexes and statistics are captured too, but
  as separate appended sections rather than folded into the `CREATE TABLE`
  statement itself.
- MSSQL server-scoped DDL triggers are not extracted, only database-level
  DML/DDL triggers (covered by `sys.sql_modules`).
- `sys.extended_properties` extraction (MSSQL) covers object- and
  column-level properties (class 1) only — database- and schema-level
  properties are not collected.
- MSSQL replication extraction covers publications and their articles
  only (best-effort, requires `dbo.syspublications`/`dbo.sysarticles` to
  exist and be readable) — subscriber enumeration is intentionally left
  out since subscription table shapes vary too much across SQL Server
  versions/topologies to guess at reliably.
- Oracle `DatabaseLinks` extraction requires privileges on `SYS.LINK$`
  (or equivalent); without them, that object type is skipped with a
  warning rather than failing the whole run.
- Linked server / database link passwords are never extracted (not
  readable from the catalog) — the generated script has a placeholder
  that must be filled in manually if ever used to recreate the link.
- Lineage edges are inferred via text/regex matching (plus structural FK
  data), not a real T-SQL/PL-SQL parser — see "The catalog / lineage
  site" above.
- History/heatmap/point-in-time only cover the mined commit window
  (`HISTORY_LIMIT`, default 250 commits) and only reconstruct individual
  objects' DDL, not a full historical catalog snapshot — see "History,
  heatmap and point-in-time" above.

Every optional/best-effort extraction step (extended properties, FKs,
checks, indexes, statistics, replication) degrades independently: a
failure on one is logged as a warning and the rest of that database's
extraction proceeds normally.
