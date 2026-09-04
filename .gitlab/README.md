# GitLab CI pipeline

This document is the canonical explanation of the CI pipeline defined by the
root [`.gitlab-ci.yml`](../.gitlab-ci.yml) and the modules under this
directory. The YAML files themselves stay close to bare configuration -
implementation rationale, trade-offs, and cross-job relationships live here
instead, so there's exactly one place to update when the pipeline's design
changes. For product/config docs (what SyncSQL does, `config/servers.json`
schema, the catalog site) see the root [`README.md`](../README.md); for the
`syncsql` CLI's own command reference see
[`cli/docs/cli.md`](../cli/docs/cli.md).

## Modules

`.gitlab-ci.yml` only declares the shared `stages`, shared `variables`, and
an `include:` of four modules under `.gitlab/ci/`:

| Module         | Stages                                              | Jobs |
|----------------|------------------------------------------------------|------|
| `cli.yml`      | `cli-lint`, `cli-test`, `cli-build`, `cli-publish`   | `cli-lint`, `cli-test`, `cli-build`, `cli-publish` |
| `extract.yml`  | `validate`, `extract`                               | `validate-config`, `extract-server` |
| `sync.yml`     | `sync`                                              | `sync-database-objects` (a thin wrapper around [`../scripts/Publish-SyncSqlObjects.ps1`](../scripts/Publish-SyncSqlObjects.ps1)) |
| `pages.yml`    | `pages`                                             | `pages` |

Splitting this way keeps each concern - building the CLI, extracting from the
fleet, publishing to git, and building/deploying the site - in its own file,
while the root file stays a short table of contents.

### Sharing job config across modules: `extends`, not YAML anchors

`extract.yml` defines two hidden jobs used by other modules:

- `.syncsql_cli` - the common `image`/`before_script` for any job that needs
  the `syncsql` CLI installed from Nexus (used by `validate-config` and
  `extract-server` in `extract.yml`, and by `sync-database-objects` in
  `sync.yml`).
- `.sync_rules` - the common `rules:` restricting a job to
  `schedule`/`web`/`trigger` pipeline sources (used by `extract-server` and
  `sync-database-objects`, and by `pages` in `pages.yml`).

Jobs pull these in with GitLab's `extends:` keyword rather than plain YAML
anchors/aliases (`&foo`/`*foo`, as `cli.yml`'s own `.cli_dotnet` template
uses internally). YAML anchors are resolved per-file at parse time, so they
can't be referenced from a different included file; `extends` is a
GitLab-specific keyword resolved after all `include:`d files are merged into
one configuration, so it works regardless of which module defines the
hidden job and which module(s) consume it. If you add a job that needs
either template, reach for `extends:` (a list, if you need both) rather than
duplicating the config or trying to share a YAML anchor across files.

## Pipeline flow

```mermaid
flowchart LR
    subgraph cli["cli.yml — builds the tool"]
        direction LR
        clint[cli-lint] --> ctest[cli-test] --> cbuild[cli-build] --> cpub["cli-publish\n(default branch only)"]
    end

    subgraph extract["extract.yml"]
        direction LR
        vc[validate-config] --- es["extract-server\n(one job per server, parallel)"]
    end

    subgraph sync["sync.yml"]
        sdo[sync-database-objects]
    end

    subgraph pages["pages.yml"]
        pg[pages]
    end

    cpub -.->|"published syncsql\ntool on Nexus"| es
    cpub -.-> sdo
    es --> sdo --> pg
```

`cli-publish` doesn't run in the same pipeline as `extract-server`/
`sync-database-objects` most of the time - it publishes a new `syncsql`
version to Nexus whenever `cli/` changes on the default branch, and
`extract-server`/`sync-database-objects` simply install whatever version is
*currently* published there when they next run. The dotted arrows above
mark that indirect, Nexus-mediated dependency, not a same-pipeline `needs:`.

## Trigger rules

- `cli-lint`/`cli-test`/`cli-build`: run on merge requests and pushes, but
  only when `cli/**/*` or `grammar/**/*` changed.
- `cli-publish`: runs only on the default branch, only when `cli/**/*` or
  `grammar/**/*` changed.
- `validate-config`: runs on merge requests and pushes (any change) - it's
  cheap and has no database/git credential requirements, so it's a useful
  fast check even outside `cli/` changes.
- `extract-server`, `sync-database-objects`, `pages`: only run for
  `schedule` or manually-triggered `web`/API pipeline sources (the
  `.sync_rules` hidden job), never on ordinary branch pushes or merge
  requests. Create a schedule under **CI/CD > Schedules** pointing at this
  project to run them regularly.

## Job-by-job

### `cli-lint` / `cli-test` / `cli-build` / `cli-publish` (`cli.yml`)

Lint (`dotnet format --verify-no-changes`), test, and build the `cli/` solution.
`cli-publish` packs `SyncSql.Cli` and pushes it to
the Nexus feed named by `NEXUS_NUGET_SOURCE_URL`, authenticated with
`NEXUS_API_KEY`; `Directory.Build.props`' `<Version>` is the single source of
truth for the published version number - bump it there to cut a new release.

`SyncSql.Lineage.Oracle` analyzes Oracle DDL with a real ANTLR4 PL/SQL parser,
but none of these jobs generate it: the generated C# parser is committed under
`grammar/`, built through a project reference, and included in the CLI tool
package. CI needs neither Java nor an ANTLR tool JAR, and there is no separately
published parser package to coordinate.

None of these jobs touch git, databases, or the fleet - they only build and
publish the CLI tool itself. See [`cli/docs/cli.md`](../cli/docs/cli.md) for
what the tool does once installed.

### `validate-config` (`extract.yml`)

Runs `syncsql validate-config` against `config/servers.json` (or
`config/servers.example.json`, if you haven't created a real config yet) to
check it parses and satisfies the schema. No database or git credentials
needed - this is a fast sanity check for merge requests and pushes.

### `extract-server` (`extract.yml`)

The only jobs that touch your databases. One job **per server**, run in
parallel via a GitLab `parallel: matrix:` over `SERVER_NAME`. Each instance
runs:

```
syncsql sync --config "$CONFIG_PATH" --server-include "^${SERVER_NAME}$" \
  --staging-root "$EXTRACTED_OBJECTS_DIR" --metrics-snapshot-root "$METRICS_SNAPSHOT_DIR" \
  --db-user "${SERVER_NAME}=$(printenv "${SERVER_NAME}_DB_USER")" \
  --db-password "${SERVER_NAME}=$(printenv "${SERVER_NAME}_DB_PASSWORD")"
```

purely locally - no git operation happens here or anywhere in the CLI.

**Credentials are passed as parameters, not inherited from the job's
environment.** The masked CI/CD variables are named after the server -
`<SERVER_NAME>_DB_USER` / `<SERVER_NAME>_DB_PASSWORD` - and `printenv` picks
the right pair and hands it to the CLI. GitLab logs the *unexpanded* script
line, so the values never reach the job log, and the CLI needs nothing in
its environment, which is what makes the same command runnable by hand.
Command-line arguments are readable by other processes on the same host,
though, so on a shared runner prefer `syncsql sync --credentials-file`
with a file the job writes from a masked variable (or drop the two
`--db-*` parameters and let the CLI fall back to the environment, which
still works).

This makes `SERVER_NAME` do double duty as the server to extract *and* its
credentials key, so the matrix needs one value per entry and no second
variable. It assumes each server's `credentialsVariablePrefix` in
`config/servers.json` **equals its `name`** (as in `config/servers.example.json`).
If a config deliberately shares one prefix across several server entries,
either pass that prefix instead of `$SERVER_NAME` in the two `--db-*`
parameters, or drop them and name the CI/CD variables after the prefix so the
CLI's environment fallback resolves them.

Every instance writes to the *same* `--staging-root`/`--metrics-snapshot-root`;
this is safe because extraction always writes under `<server>/...` first
(see `ExtractedObjectFile.RelativePath`), so concurrent instances scoped to
different servers never collide. GitLab merges every instance's artifacts
together for the downstream `sync-database-objects` job - a plain job name
in `dependencies:` pulls in *all* instances of a `parallel:` job.

The `SERVER_NAME` matrix list must match every server
`config/servers.json`'s `serverSelection` would actually run for this
pipeline, and has to be kept in sync by hand whenever the fleet changes - a
server present in the config but missing from the matrix silently isn't
extracted. **For a small fleet where that upkeep isn't worth the
parallelism**, drop this job and the matrix, and instead give
`sync-database-objects` a first script line of:

```
syncsql sync --config "$CONFIG_PATH" --staging-root "$EXTRACTED_OBJECTS_DIR" \
  --metrics-snapshot-root "$METRICS_SNAPSHOT_DIR"
```

(no `--server-include`, so every server extracts; with no per-server
`--db-user`/`--db-password` to pass, credentials come from the job's
`<prefix>_DB_USER`/`<prefix>_DB_PASSWORD` variables) ahead of its existing
publish script - same end result, back to one sequential job doing
everything.

### `sync-database-objects` (`sync.yml`)

Publishes the merged output of every `extract-server` instance. This is the
**only** place git actually runs in the whole pipeline - and the job itself
is deliberately thin: it installs `git` (and PowerShell, if the image
doesn't already ship it) and invokes
[`../scripts/Publish-SyncSqlObjects.ps1`](../scripts/Publish-SyncSqlObjects.ps1)
with every setting as a parameter. Everything git-shaped - resolving
`config.git.*`, cloning, replacing `config.git.pathPrefix` with the merged
staged tree, committing, and pushing - lives in that script, which also
calls `syncsql metrics update` and `syncsql catalog build` for the pure,
non-git steps: folding this run's metrics into history, and rebuilding
`catalog.json` (structure, inferred lineage via a real parser per engine,
and history/heatmap/point-in-time mined from this repo's own commit
history). One commit carries the extracted objects, `catalog.json`, and the
updated `metrics/` tree together.

Keeping it in a script rather than an inline YAML block means the exact
same publish can be read, reviewed, and run by hand from a workstation:

```powershell
pwsh ./scripts/Publish-SyncSqlObjects.ps1 `
  -ExtractedObjectsDir ./syncsql-output/objects `
  -MetricsSnapshotDir ./syncsql-output/metrics-snapshot `
  -ConfigPath ./config/servers.json `
  -SkipPush
```

| Parameter | Default | What it does |
|-----------|---------|--------------|
| `-ExtractedObjectsDir` | *(required)* | The tree to publish - `syncsql sync --staging-root`'s output. |
| `-MetricsSnapshotDir` | *(none)* | This run's `--metrics-snapshot-root`. Omitted or missing: the metrics history update is skipped. |
| `-ConfigPath` | *(none)* | `config/servers.json`, read only to default the git settings below. |
| `-RemoteUrl` | config `git.remoteUrl` | Repository published to. The CI job passes the `CI_SERVER_*`-derived self-repo URL when the config leaves it blank. |
| `-Branch` | config, then `main` | Branch cloned, committed to, and pushed. Created from the default branch if the remote doesn't have it yet. |
| `-PathPrefix` | config, then `objects` | Folder inside the clone the object tree replaces. |
| `-CommitUserName` / `-CommitUserEmail` / `-CommitMessage` | config, then the SyncSQL defaults | Commit identity and message. |
| `-MetricsHistoryDirName` | `metrics` | Folder inside the clone holding the metrics history tree, kept outside `-PathPrefix`. |
| `-CatalogFileName` | `catalog.json` | Name of the catalog written inside `-PathPrefix`. |
| `-HistoryLimit` | `250` | Clone depth and `catalog build --history-limit`. |
| `-MetricsHistoryLimit` | `90` | `metrics update --history-limit`. |
| `-MaxVersionsPerObject` / `-MaxHistoryContentCalls` / `-MaxCoChangeCommitSize` | `15` / `1500` / `40` | Passed straight through to `catalog build`. |
| `-PushToken` | `SYNCSQL_PUSH_TOKEN`, `GIT_PUSH_TOKEN`, then `CI_JOB_Maintainer_Token` | The one deliberate environment fallback - see below. |
| `-SyncSqlCommand` | `syncsql` | The CLI to invoke, e.g. an absolute path to a locally built one. |
| `-CloneDirectory` | a fresh temp directory | Where the target repo is cloned; removed afterwards unless `-KeepCloneDirectory`. |
| `-DotEnvPath` | *(none)* | Where to write the `PATH_PREFIX`/`GIT_BRANCH` dotenv the `pages` job consumes. |
| `-SkipPush` | off | Do everything except commit and push - a real dry run. |

Notable details:

- **`config.git.*` defaults** (branch `main`, path prefix `objects`, etc.)
  match what SyncSQL has always used - see the root README's Configuration
  section. Precedence is parameter → config file → default, so CI can pin a
  value without editing the config, and a config-only setup keeps working.
- **Push token**: a secret is the one thing the script still reads from its
  environment when it isn't passed (`SYNCSQL_PUSH_TOKEN`, `GIT_PUSH_TOKEN`,
  `CI_JOB_Maintainer_Token`) - precisely so it never has to appear in a
  command line. It reaches `git` through a credential helper that reads it
  back out of the script's own environment: never a `git` argument, never
  written to disk, never embedded in the remote URL, so it can't leak via a
  process listing, `git remote -v`, or shell history.
- **Wipe and repopulate**: the target directory (`-PathPrefix`) is
  deleted and rewritten from scratch on every run, so objects dropped from
  the source database (or excluded by an updated filter) show up as
  deletions in git rather than lingering forever.
- **`sync.env` dotenv report**: `-DotEnvPath` writes `PATH_PREFIX`/`GIT_BRANCH`
  as a GitLab `dotenv` artifact so the downstream `pages` job knows where to
  find `catalog.json` and which branch tip to fetch, without hardcoding either.
- **PowerShell version**: the script targets **Windows PowerShell 5.1**, so a
  stock Windows runner or workstation runs it with no PowerShell install at
  all - and it runs unchanged on PowerShell 7+, which is what this Linux image
  uses. Nothing in it relies on a 7-only language feature or cmdlet parameter.
- **PowerShell in the job**: the `.NET SDK` image may or may not ship `pwsh`
  depending on the tag, so the job installs it as a dotnet tool when
  `command -v pwsh` finds nothing. On a runner with no nuget.org access,
  mirror the `PowerShell` package in Nexus and add
  `--add-source "$NEXUS_NUGET_SOURCE_URL"` to that install line. On a Windows
  runner neither applies: drop that line and invoke the script with
  `powershell -NoProfile -NonInteractive -ExecutionPolicy Bypass -File ...`.

This design is a deliberate split: the CLI (`syncsql`) is a pure,
git-agnostic data pipeline you can run and test anywhere, and this one
script is the single place that decides how and where results get
published.

### `pages` (`pages.yml`)

Builds `site/` (React/Vite) and publishes it as a GitLab Pages site. Reads
`catalog.json` straight out of the git checkout - `sync-database-objects`
just committed it there - rather than via a separate CI artifact. The
`pages` job name is what makes GitLab treat this as a Pages deployment; its
artifacts must be a directory named exactly `public` at the project root.

`sync-database-objects` just pushed a new commit (with `catalog.json` in
it), but this job's own checkout was taken from the pipeline's *original*
commit - so its `before_script` fetches and hard-resets to the branch tip
first, using the `GIT_BRANCH` (falling back to `$CI_COMMIT_REF_NAME`) and
`PATH_PREFIX` (falling back to `objects`) from `sync-database-objects`'s
`sync.env` dotenv report, and copies `$CATALOG_FILE_NAME` (falling back to
`catalog.json`) out of that folder.

## Required CI/CD variables

Set these under **Settings > CI/CD > Variables** (masked + protected):

| Variable                            | Purpose                                                                                                    |
|--------------------------------------|-------------------------------------------------------------------------------------------------------------|
| `CI_JOB_Maintainer_Token`            | A project access token with the **Maintainer** role and `write_repository` scope, read by `Publish-SyncSqlObjects.ps1` to push extracted objects back into this project. The built-in `CI_JOB_TOKEN` cannot push commits, hence a dedicated token. Falls back to `GIT_PUSH_TOKEN`, and to the script's `-PushToken` parameter if you'd rather pass it in. |
| `<SERVER_NAME>_DB_USER` / `_DB_PASSWORD` | One pair per server entry in `config/servers.json`, named after that server's `name` (which `extract-server` also uses as its `credentialsVariablePrefix`). The job reads them with `printenv` and passes them to the CLI as `--db-user`/`--db-password` parameters. |
| `NEXUS_NUGET_SOURCE_URL`             | NuGet v3 feed URL used by `validate-config`/`extract-server`/`sync-database-objects` to install the published `syncsql` tool and by `cli-publish` to publish it. |
| `NEXUS_API_KEY`                      | API key/token with publish rights to that feed - only needed by `cli-publish`. |
| `SYNC_REMOTE_URL`                    | Optional. Repository `sync-database-objects` publishes to, passed as the script's `-RemoteUrl`. Unset, the job passes this project's own URL built from `CI_SERVER_PROTOCOL`/`CI_SERVER_HOST`/`CI_PROJECT_PATH`. |

`CI_JOB_Maintainer_Token` is only needed if `git.remoteUrl` is left blank in
`config/servers.json` (the default, self-repo target). If you point
`git.remoteUrl` at a different project, it needs Maintainer/
`write_repository` access there instead.

Every other setting is a plain pipeline variable in `.gitlab-ci.yml`, passed
down as a CLI/script parameter by the job that needs it: `CONFIG_PATH`,
`EXTRACTED_OBJECTS_DIR`, `METRICS_SNAPSHOT_DIR`, `METRICS_HISTORY_DIR_NAME`,
`CATALOG_FILE_NAME`, `PUBLISH_SCRIPT`, `HISTORY_LIMIT`,
`METRICS_HISTORY_LIMIT`, `MAX_VERSIONS_PER_OBJECT`,
`MAX_HISTORY_CONTENT_CALLS`, and `MAX_CO_CHANGE_COMMIT_SIZE`. None of them
is read out of the environment by the CLI or the publish script - which is
what lets the same commands run on a workstation, where the tools' own
defaults (`./syncsql-output/...`) take over.

Optional: `HISTORY_LIMIT` (default `250`, set in `.gitlab-ci.yml`) controls
how many commits get mined for the heatmap/co-change/point-in-time features
baked into `catalog.json`, and how deep `sync-database-objects`'s own clone
of the target repo goes so that history is actually there to mine - see the
root README's "History, heatmap and point-in-time" section. `catalog.json`
is generated and committed *by* `sync-database-objects`, alongside the
extracted objects themselves, so it's versioned right along with them
rather than living only as a separate CI artifact.

Optional: `METRICS_HISTORY_LIMIT` (default `90`, set in `.gitlab-ci.yml`)
controls how many daily volume/index/optimizer-statistics snapshots are kept
per table (the `metrics/` tree, outside `git.pathPrefix`) - see the root
README's "Volatile metrics" section. This data changes every run by nature,
so it's tracked separately from each object's own version history rather
than bloating it.

## Running it

Create a schedule under **CI/CD > Schedules** pointing at this project (any
interval you like); `extract-server`, `sync-database-objects`, and `pages`
only run for `schedule` (or manually-triggered `web`/API) pipeline sources,
as described above. Once it's run once, find the site URL under
**Settings > Pages**.
