# SyncSQL

Scheduled extraction of database objects (stored procedures, views,
functions, triggers, tables, schemas, synonyms, linked servers / database
links) from a fleet of **MSSQL** and **Oracle** servers into this repository
— one file per object, one commit per run, diffable like any other source
code — followed by a best-effort structure/lineage analysis published as a
browsable **React site on GitLab Pages**.

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
        |                           sys.tables, sys.servers, sys.synonyms,
        |                           optionally sys.extended_properties)
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
  with the staged tree (dropped objects show        best-effort lineage edges, writes
  up as deletions), commit, push                     catalog/catalog.json
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
  `objectNames` include/exclude regex lists, and an `objectTypes` list.
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

## Running the pipeline

`.gitlab-ci.yml` defines four jobs across four stages:

- **validate-config** (`validate`): runs on merge requests / pushes, just
  checks that `config/servers.yml` (or the example file, if you haven't
  added one yet) parses and satisfies the schema. No database or git
  credentials needed.
- **sync-database-objects** (`sync`): the actual extraction, and the only
  job that touches your databases and pushes to git. Produces the
  `extracted-objects/` artifact consumed by the next stage.
- **analyze-catalog** (`analyze`): runs `Build-Catalog.ps1` over
  `extracted-objects/` to produce `catalog/catalog.json` (structure +
  best-effort lineage graph).
- **pages** (`pages`): builds `site/` (React/Vite) with that `catalog.json`
  and publishes it as this project's GitLab Pages site.

The last three only run for `schedule` (and manually-triggered `web`/API)
pipeline sources — create a schedule under **CI/CD > Schedules** pointing
at this project. Once it's run once, find the site URL under **Settings >
Pages**.

## The catalog / lineage site

`site/` is a small React + TypeScript + Vite app (source checked into this
repo, built fresh by the `pages` job on every scheduled run):

- A searchable tree (server → database → object type → schema → object) in
  the sidebar.
- An object detail page: qualified name, `sys.extended_properties`
  descriptions (object + column level, MSSQL only) if present,
  syntax-highlighted DDL, and "depends on" / "used by" lineage lists with an
  embedded neighborhood graph.
- A full lineage explorer (`/#/lineage`), filterable by server/database,
  rendered with `@xyflow/react` + `dagre` auto-layout.

**Lineage is inferred, not parsed.** `Build-Catalog.ps1` regex-matches
identifiers found in each object's DDL text against every other known
object name (qualified `schema.object` references, plus unqualified names
when exactly one candidate exists in the same database or, for linked
servers/DB links, the same server). It is a reasonable starting point for
exploration, not a certified lineage report — it will miss dynamic SQL and
four-part cross-linked-server references, and can occasionally produce a
false-positive edge when an identifier collides with an unrelated object
name. The site says as much on its overview page.

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
to preview the real site locally with `npm run dev` inside `site/`.

## Known limitations (v1)

- MSSQL table DDL is reconstructed from `INFORMATION_SCHEMA`/catalog
  views (columns, identity, defaults, primary key) since SQL Server
  doesn't store table definitions as text the way it does for
  procedures/views. Indexes, foreign keys and check constraints are not
  included yet.
- MSSQL server-scoped DDL triggers are not extracted, only database-level
  DML/DDL triggers (covered by `sys.sql_modules`).
- `sys.extended_properties` extraction (MSSQL) covers object- and
  column-level properties (class 1) only — database- and schema-level
  properties are not collected. It's best-effort by design: any failure
  (permissions, restricted edition, etc.) is logged as a warning and the
  rest of that database's extraction proceeds normally without it.
- Oracle `DatabaseLinks` extraction requires privileges on `SYS.LINK$`
  (or equivalent); without them, that object type is skipped with a
  warning rather than failing the whole run.
- Linked server / database link passwords are never extracted (not
  readable from the catalog) — the generated script has a placeholder
  that must be filled in manually if ever used to recreate the link.
- Lineage edges are inferred via text/regex matching, not a real
  T-SQL/PL-SQL parser — see "The catalog / lineage site" above.
