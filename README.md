# SyncSQL

Scheduled extraction of database objects (stored procedures, views,
functions, triggers, tables, schemas, synonyms, linked servers / database
links) from a fleet of **MSSQL** and **Oracle** servers into their own
git repository — one file per object, one commit per run, diffable like
any other source code.

Built as a GitLab CI scheduled pipeline driven entirely by PowerShell
(pwsh), so the same script runs unmodified on any runner that can pull the
`mcr.microsoft.com/powershell` image, with no OS-specific tooling and no
native Oracle client install required.

## How it works

```
config/servers.yml  --defines-->  which servers/databases/schemas/objects
        |
        v
Bootstrap-Dependencies.ps1   (SqlServer + powershell-yaml modules,
                               Oracle.ManagedDataAccess.Core driver)
        |
        v
Export-DatabaseObjects.ps1
        |
        +--> SyncSql.MsSql.psm1    (Invoke-Sqlcmd against sys.sql_modules,
        |                           sys.tables, sys.servers, sys.synonyms)
        +--> SyncSql.Oracle.psm1   (DBMS_METADATA.GET_DDL via the managed
        |                           ADO.NET driver, no Instant Client)
        |
        v
staging/<server>/<database>/<objectType>/[<schema>/]<object>.sql
        |
        v
SyncSql.Git.psm1  -->  clone target repo, replace <pathPrefix>/ with the
                        staged tree (so dropped objects show up as
                        deletions), commit, push
```

Every extracted file gets a small static header (server / database / type
/ object name) and nothing else — no timestamps — so re-running the
pipeline with no underlying database changes produces **zero diff**.

## Configuration

Copy `config/servers.example.yml` to `config/servers.yml` and edit it.
Nothing in that file is secret: it lists server hostnames and the regex
filters that decide what gets extracted. See the comments in the example
file for the full schema; in short:

- `git`: where extracted objects get pushed (defaults to the project
  running the pipeline if `remoteUrl` is left blank), which branch, and
  what path prefix inside that repo.
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

| Variable                          | Purpose                                                             |
|------------------------------------|----------------------------------------------------------------------|
| `GIT_PUSH_TOKEN`                   | Token with write access to the target repository                    |
| `<PREFIX>_DB_USER` / `_DB_PASSWORD`| One pair per server entry in `config/servers.yml`                    |

If `git.remoteUrl` is left blank, extracted objects are pushed back into
the project this pipeline runs in, using the standard `CI_SERVER_*`
predefined variables to build the remote URL — `GIT_PUSH_TOKEN` still
needs write access to that project.

## Running the pipeline

`.gitlab-ci.yml` defines two jobs:

- **validate-config**: runs on merge requests / pushes, just checks that
  `config/servers.yml` (or the example file, if you haven't added one
  yet) parses and satisfies the schema. No database or git credentials
  needed.
- **sync-database-objects**: the actual extraction. Only runs for
  `schedule` (and manually-triggered `web`/API) pipeline sources — create
  a schedule under **CI/CD > Schedules** pointing at this project.

## Running locally

```pwsh
pwsh ./src/Bootstrap-Dependencies.ps1
$env:SQLPROD01_DB_USER = '...'
$env:SQLPROD01_DB_PASSWORD = '...'
pwsh ./src/Export-DatabaseObjects.ps1 -ConfigPath ./config/servers.yml -SkipGit
```

`-SkipGit` leaves the extracted files under a temp staging directory
(printed in the log) instead of publishing them, which is useful for
reviewing output before wiring up a real target repository.

## Known limitations (v1)

- MSSQL table DDL is reconstructed from `INFORMATION_SCHEMA`/catalog
  views (columns, identity, defaults, primary key) since SQL Server
  doesn't store table definitions as text the way it does for
  procedures/views. Indexes, foreign keys and check constraints are not
  included yet.
- MSSQL server-scoped DDL triggers are not extracted, only database-level
  DML/DDL triggers (covered by `sys.sql_modules`).
- Oracle `DatabaseLinks` extraction requires privileges on `SYS.LINK$`
  (or equivalent); without them, that object type is skipped with a
  warning rather than failing the whole run.
- Linked server / database link passwords are never extracted (not
  readable from the catalog) — the generated script has a placeholder
  that must be filled in manually if ever used to recreate the link.
