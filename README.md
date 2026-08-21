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
        v
SyncSql.Git.psm1  (still inside the sync stage)
  clone this project (deep enough to mine, see -HistoryLimit), replace
  <pathPrefix>/ with the staged tree (dropped objects show up as
  deletions), THEN run Build-Catalog.ps1 against that same checkout -
  structure, best-effort lineage, and history/heatmap/point-in-time mined
  from this project's own git log - writing <pathPrefix>/catalog.json
  right alongside the objects it describes, so ONE commit carries both and
  catalog.json is versioned (diffable, browsable at any past commit) the
  same way the objects themselves are. Commit, push.
        |
        v
site/ (React + Vite)                                    [CI stage: pages]
  fetches the branch tip (to see the commit sync just pushed), reads
  <pathPrefix>/catalog.json straight out of that checkout, builds with it
  copied into public/data/, publishes as a GitLab Pages site
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

Optional: `HISTORY_LIMIT` (default `250`) controls how many commits get
mined for the heatmap / co-change / point-in-time features baked into
`catalog.json` — see "History, heatmap and point-in-time" below.

## Running the pipeline

`.gitlab-ci.yml` defines three jobs across three stages:

- **validate-config** (`validate`): runs on merge requests / pushes, just
  checks that `config/servers.yml` (or the example file, if you haven't
  added one yet) parses and satisfies the schema. No database or git
  credentials needed.
- **sync-database-objects** (`sync`): the actual extraction, the only job
  that touches your databases, and the one that builds and commits
  `catalog.json` alongside the extracted objects (see "How it works"
  above) before pushing. Produces the `extracted-objects/` artifact
  (handy for debugging a run without needing to dig through git history)
  and a `dotenv` report (`PATH_PREFIX`/`GIT_BRANCH`) so the `pages` job
  knows where to find `catalog.json` in the checkout.
- **pages** (`pages`): fetches the branch tip (to see the commit
  sync-database-objects just pushed), builds `site/` (React/Vite) with
  the `catalog.json` it finds there, and publishes it as this project's
  GitLab Pages site.

The last two only run for `schedule` (and manually-triggered `web`/API)
pipeline sources — create a schedule under **CI/CD > Schedules** pointing
at this project. Once it's run once, find the site URL under **Settings >
Pages**.

## The catalog / lineage site

`site/` is a React + TypeScript + Vite app (source checked into this repo,
built fresh by the `pages` job on every scheduled run), styled as a dense
data-terminal with a light/dark toggle (top right; light is the default -
see "Theme" below):

- **Overview** — object counts, the 10 most recently changed objects, the
  most-referenced tables (direct incoming edges and indirect/transitive
  reachability, capped to one hop across a linked-server boundary), a
  change-frequency heatmap, and objects that tend to change together in
  the same commit.
- **Explorer** — a sortable, filterable table listing every object; it's the
  primary way to browse the catalog (there is no separate tree sidebar - see
  "Explorer replaces the sidebar" below).
- **Object detail** — qualified name, `sys.extended_properties` descriptions
  (object + column level, MSSQL only), the full structural column list with
  data types (Tables/Views), the DDL, structured panels for any Foreign Keys
  / Check Constraints / Indexes / Statistics sections, an **Access** panel
  (see "Grant mapping" below), a change-history list with a point-in-time
  viewer, and "depends on" / "used by" lineage lists annotated with
  best-effort column tags (expandable past the first few) with an embedded
  neighborhood graph.
- **Lineage** (`/#/lineage`) — a full graph explorer rendered with
  `@xyflow/react` + `dagre` auto-layout, with two modes (tabs):
  - **Browse** — the object filter bar drives which objects are shown.
    Clicking a node drills the graph into that object's own neighborhood in
    place (breadcrumb trail, Back button, adjustable 1/2/3-hop radius)
    rather than leaving the page; double-click opens that object's full
    detail page. An object page's "Open in full lineage explorer" link
    lands here with an actual filter token seeded for that object, so
    clearing the drill-down focus narrows back to it instead of dumping out
    to the whole catalog.
  - **Access** — search by grantee (user, role or group) to see every
    object they have a GRANT or DENY permission on, down to the column when
    scoped that way (see "Grant mapping" below); matches are listed in a
    table and rendered in the same graph, so you can drill from "what can
    this principal touch" straight into how those objects relate.

  Edges carrying a known column-level reference (see "Column dependency
  tracking" below) are highlighted, labeled with up to 3 referenced column
  names, and clickable — click one to open a detail panel with the full
  column list for that edge.
- **History** — a global commit timeline of everything the pipeline has
  changed, expandable per commit.

All of Explorer/Lineage's filter bar share one GitLab-style filter bar: type
to get attribute suggestions (server, database, schema, type, name,
description), pick an operator (is / is not / contains / is in / is not
in), then pick from suggested values pulled from the catalog. Suggestion
lookups are capped and debounced, and committed filters (not keystrokes)
are what actually re-filter the object list, so it stays responsive on
large catalogs.

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

### Explorer replaces the sidebar

Earlier versions of the site had an always-open (later toggleable) tree
sidebar (Server → Database → Schema → Type → Object) alongside Explorer.
It has been removed: Explorer's filter bar plus sortable columns cover the
same browsing need with less UI, and every other page (Lineage, Overview,
History) links directly to object detail pages rather than requiring the
tree.

### Theme

A light/dark toggle lives in the top right of every page (`lib/ThemeContext.tsx`),
persisted to `localStorage`. Light is the default. DDL/code blocks are the
one deliberate exception — always rendered dark (matching their
`highlight.js` syntax theme) regardless of which site theme is active, so
SQL stays legible with one consistent look. The Lineage graph (`@xyflow/react`)
follows the site theme too, defaulting to light along with everything else.

### Grant mapping

`Build-Catalog.ps1` parses a per-object "Grants" section (attached by the
extraction backend, best-effort - see "Known limitations" below) into a
structured `grants` list on each catalog node: grantee, grantee type (MSSQL
only - `SQL_USER`, `DATABASE_ROLE`, `WINDOWS_GROUP`, ...), permission
(`SELECT`, `EXECUTE`, ...), state (`GRANT`/`DENY` - MSSQL only, Oracle has
no DENY concept), and the column when the grant was scoped to one rather
than the whole object.

- MSSQL: `sys.database_permissions` (object/column-level, class =
  `OBJECT_OR_COLUMN`) joined to `sys.database_principals` for the grantee
  and its type.
- Oracle: `ALL_TAB_PRIVS` (object-level) and `ALL_COL_PRIVS`
  (column-level) for every object owned by each extracted schema.

Every object's detail page has an **Access** panel (below the DDL and its
appended sections, above Change history) listing its own grants; the
Lineage page's **Access** tab flips the query around - search by grantee to
see every object (and, when scoped, column) that principal can touch, both
in a table and in the graph. Both degrade to "no grants" rather than
failing extraction when the underlying permissions view isn't accessible to
the connecting account.

### Column dependency tracking

Tables and views get a full structural column list (name + data type),
independent of whether a column happens to have an
`sys.extended_properties`/documentation entry - `sys.columns` (MSSQL) /
`ALL_TAB_COLUMNS` (Oracle). `Build-Catalog.ps1` then re-scans each inferred
edge's source DDL for qualified `alias.column` references (resolving
simple `FROM`/`JOIN` aliases, plus the bare/qualified object name itself)
against the target's column list, and records which of the target's
columns are actually referenced on that edge. This is the same
best-effort, regex-based approach the rest of lineage inference uses, not a
certified column-level lineage report - it will miss dynamic SQL, `SELECT
*`, and computed/aliased column expressions.

This shows up as column tags next to each entry in an object's "depends
on"/"used by" lists, and as highlighted, labeled edges in the Lineage
graph.

### History, heatmap and point-in-time

A static Pages site can't run live `git` queries, so `Build-Catalog.ps1`
mines history *during the sync CI stage* instead, right before committing:
`Export-DatabaseObjects.ps1` clones the target repo deeply enough
(`-HistoryLimit` commits, default 250 — override via the `HISTORY_LIMIT`
CI variable) for `Build-Catalog.ps1` to mine it (`-RepoRoot`), and the
resulting `catalog.json` is written straight into that same checkout and
committed alongside the objects it describes - so it's versioned in git
history too, not just a CI artifact that disappears after the job expires.
Mining history produces:

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
Running with `-SkipGit` (no git publish, so no repo to mine and nowhere to
commit `catalog.json` into) simply omits all of this — empty history, zero
change counts — rather than failing.

To work on the site locally:

```sh
cd site
npm install
npm run dev
```

`site/public/data/catalog.json` ships a small demo fixture so `npm run dev`
has something to render before any pipeline has actually run; replace it
with a real one (see below) to preview actual data.

## Running the extraction locally

```pwsh
pwsh ./src/Bootstrap-Dependencies.ps1
$env:SQLPROD01_DB_USER = '...'
$env:SQLPROD01_DB_PASSWORD = '...'
pwsh ./src/Export-DatabaseObjects.ps1 -ConfigPath ./config/servers.yml -SkipGit
```

`-SkipGit` leaves the extracted files under a temp staging directory
(printed in the log) instead of publishing them - so no `catalog.json` is
built in this mode either (there's no git checkout to write it into or
mine history from). To build one for local preview, feed that staging
directory into
`Build-Catalog.ps1 -ObjectsRoot <dir> -OutputPath ./site/public/data/catalog.json`
(add `-RepoRoot`/`-PathPrefix` pointed at a real git checkout of your
target repo to include history), then `npm run dev` inside `site/`.

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
- Column dependency tags on lineage edges are likewise regex-based (alias
  resolution over `FROM`/`JOIN` text), not a real parser — see "Column
  dependency tracking" above.
- Grant extraction (MSSQL `sys.database_permissions`, Oracle
  `ALL_TAB_PRIVS`/`ALL_COL_PRIVS`) only covers object/column-level grants
  on the extracted objects themselves — server/database-level permissions,
  role membership, and (Oracle) whether a grantee is itself a user or a
  role are out of scope. See "Grant mapping" above.
- History/heatmap/point-in-time only cover the mined commit window
  (`HISTORY_LIMIT`, default 250 commits) and only reconstruct individual
  objects' DDL, not a full historical catalog snapshot — see "History,
  heatmap and point-in-time" above.

Every optional/best-effort extraction step (extended properties, grants,
full column lists, FKs, checks, indexes, statistics, replication) degrades
independently: a failure on one is logged as a warning and the rest of
that database's extraction proceeds normally.
