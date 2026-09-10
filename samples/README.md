# Sample databases

A complete, throwaway database fleet for SyncSQL: two containers, twenty samples
drawn from Microsoft's and Oracle's own public sample repositories, one command to
build it, one to run the CLI over it, and one to check that what came out is what
should have come out.

It exists because SyncSQL's interesting behaviour - lineage inference across two
dialects, linked-server hops, orphaned-reference detection - only shows itself
against real schemas. Toy fixtures in unit tests cannot tell you whether the
PL/SQL analyzer still resolves `emp_details_view` to all six tables it selects
from. AdventureWorks, Wide World Importers, HR, CO and SH can.

```sh
samples/scripts/setup-databases.sh    # bring the fleet up and load it
samples/scripts/run-syncsql.sh        # extract it, build the catalog
samples/scripts/run-benchmark.sh      # assert the result against samples/expected/
```

Every script has a PowerShell twin (`.ps1`) that takes the same options as
PowerShell parameters, so Visual Studio users on Windows are not pushed through
WSL.

## Contents

| | |
|---|---|
| [`samples.json`](samples.json) | The manifest. Every sample, where it comes from, how it installs, which tier it is in. Everything else reads it. |
| [`docker/`](docker) | `docker-compose.yml` for the two engines, plus `.env.example`. |
| [`mssql/`](mssql) | One folder per demo in [microsoft/sql-server-samples/samples/demos](https://github.com/microsoft/sql-server-samples/tree/master/samples/demos), plus the two base databases those demos read from. |
| [`oracle/`](oracle) | One folder per schema in [oracle-samples/db-sample-schemas](https://github.com/oracle-samples/db-sample-schemas). |
| [`config/`](config) | `servers.samples.json` - the SyncSQL config that points at the two containers. |
| [`expected/`](expected) | What the benchmark asserts: hand-written expectations, and the recorded baseline. |
| [`scripts/`](scripts) | The three commands above, in bash and PowerShell. |

Nothing from upstream is vendored here. Each sample folder holds a README, a
pointer to its upstream path, and - where one is needed - a short SyncSQL prelude
script; the SQL itself is fetched at provisioning time into the git-ignored
`samples/.cache/`. That keeps roughly 100 MB of third-party CSVs, media and
backups out of this repository's history, and keeps the samples honestly
attributed to the projects that maintain them.

## Prerequisites

| Tool | Needed for |
|------|------------|
| [Docker](https://docs.docker.com/get-docker/) with Compose v2 | the two database containers |
| `git`, `curl` | fetching the upstream sources and the sample backups |
| [`jq`](https://jqlang.github.io/jq/) | only for the bash scripts - the PowerShell ones use `ConvertFrom-Json` |
| [.NET SDK 10](https://dotnet.microsoft.com/download) | running the CLI and the benchmark |
| bash 4+ | the `.sh` scripts. macOS ships bash 3.2, so `brew install bash` or use the `.ps1` twins |

About 6 GB of disk: ~500 MB of downloaded backups, ~14 MB of fetched sources, and
the rest is the two containers and their data.

## Bringing the fleet up

```sh
samples/scripts/setup-databases.sh
```

On first run this creates `samples/.env` from
[`docker/.env.example`](docker/.env.example) - throwaway passwords for throwaway
containers - then fetches the upstream sources, downloads the sample backups,
starts the containers, and installs every sample in the standard tier.

```
--engine mssql|oracle|all   Which engine to provision. Default: all.
--tier standard|heavy|all   Which tier. Default: standard.
--only  <id>[,<id>...]      Provision exactly these samples (ignores --tier).
--skip  <id>[,<id>...]      Skip these samples.
--skip-fetch                Reuse samples/.cache as-is.
--status                    What is running and what is available.
--down                      Stop the containers and delete their volumes.
```

The containers listen on **14330** (SQL Server) and **15210** (Oracle) rather than
the default ports, so a local SQL Server or Oracle install keeps working. Change
them in `samples/.env` and change [`config/servers.samples.json`](config/servers.samples.json)
to match.

### Tiers

- **standard** - installed by default. The two base SQL Server databases, four
  demos that create objects, and the three supported Oracle schemas.
- **heavy** - opt in with `--tier heavy` (or `--tier all`). Two demos whose setup
  script inserts ten million rows one at a time, and one that needs an 883 MB
  backup.
- **unsupported** - never installed. Six samples that cannot run against a
  container: Synapse-only scripts, SSMS-only artifacts, and Oracle's three
  archived schemas. Each one still has a folder, and its README says exactly why.

## What is in the fleet

### SQL Server (`SAMPLES-MSSQL`)

| Sample | Database | Tier | What it adds |
|--------|----------|------|--------------|
| [`adventure-works`](mssql/adventure-works) | `AdventureWorks2022`, `AdventureWorksDW2022` | standard | The classic OLTP and DW schemas - the deepest view/procedure graph in the fleet. |
| [`wide-world-importers`](mssql/wide-world-importers) | `WideWorldImporters`, `WideWorldImportersDW` | standard | The modern equivalent, and the base the two demos below build on. |
| [`belgrade-product-catalog-demo`](mssql/belgrade-product-catalog-demo) | `ProductCatalog` | standard | JSON columns, temporal tables, data masking, row-level security, an in-memory table. |
| [`ivs-people-register`](mssql/ivs-people-register) | `IvsPeopleRegister` | standard | Ideographic Variation Sequence collations and `OPENJSON`. |
| [`sql-graph`](mssql/sql-graph) | `WideWorldImporters` | standard | Graph node and edge tables in their own schemas. |
| [`automatic-tuning`](mssql/automatic-tuning) | `WideWorldImporters` | standard | The `dbo.report` procedure the automatic-tuning demo regresses. |
| [`lqs`](mssql/lqs) | `AdventureWorks2022` | standard | Query-only; contributes no objects of its own. |
| [`showplan`](mssql/showplan) | `memgrants` | heavy | Showplan warnings, on a table built by a ten-million-row loop. |
| [`xevents`](mssql/xevents) | `memgrants` | heavy | The same warnings through Extended Events. |
| [`columnstore`](mssql/columnstore) | `AdventureWorksDW2016` | heavy | Rowstore against clustered columnstore, on the 883 MB extended DW backup. |
| [`sqldw`](mssql/sqldw) | - | unsupported | Azure Synapse only. |
| [`plan-comparison`](mssql/plan-comparison) | - | unsupported | SSMS artifacts, no SQL. |
| [`query-tuning-assistant`](mssql/query-tuning-assistant) | - | unsupported | An SSMS walkthrough in a .zip. |
| [`azure-sql-edge-demos`](mssql/azure-sql-edge-demos) | - | unsupported | A pointer to another repository. |

### Oracle (`SAMPLES-ORACLE`, service `FREEPDB1`)

| Sample | Schema | Tier | What it adds |
|--------|--------|------|--------------|
| [`human-resources`](oracle/human-resources) | `HR` | standard | 7 tables, a six-table view, 2 procedures, 2 triggers. The clearest lineage in the fleet. |
| [`customer-orders`](oracle/customer-orders) | `CO` | standard | 7 tables and 4 views, with JSON columns. |
| [`sales-history`](oracle/sales-history) | `SH` | standard | A star schema with materialized views. |
| [`order-entry`](oracle/order-entry) | `OE` | unsupported | Archived upstream; needs Data Pump and SQL\*Loader. |
| [`online-catalog`](oracle/online-catalog) | `OC` | unsupported | Archived upstream; built inside OE. |
| [`product-media`](oracle/product-media) | `PM` | unsupported | Archived upstream; needs Oracle Multimedia, desupported since 19c. |

## Running the CLI over it

```sh
samples/scripts/run-syncsql.sh
```

Four steps, the same ones a pipeline runs, all writing into `samples/output/`:

```
validate-config -> sync -> metrics update -> catalog build -> lint
```

No git is involved anywhere: `sync` only writes local files, and `catalog build`
runs without `--repo-root`, so the history and heatmap parts of `catalog.json`
stay empty. Passwords are written to a git-ignored credentials file and passed
with `--credentials-file`, never as arguments.

```
--config <path>          Default: samples/config/servers.samples.json
--output-root <path>     Default: samples/output
--server-include <regex> Extract only matching servers. Repeatable.
--server-exclude <regex> Skip matching servers. Repeatable.
--clean                  Delete the output root first.
--skip-lint              Skip the T-SQL lint pass.
--no-build               The CLI is already built.
```

To look at the result, copy `samples/output/catalog.json` into `site/public/` and
run `npm run dev` in [`site/`](../site).

## The benchmark

```sh
samples/scripts/run-benchmark.sh
```

[`cli/tests/SyncSql.Samples.Benchmark.Tests`](../cli/tests/SyncSql.Samples.Benchmark.Tests)
runs the extraction and the catalog build against the live fleet and asserts on
both. It is part of `cli/SyncSql.slnx`, but every test in it skips itself unless
`SYNCSQL_SAMPLES=1` is set, so an ordinary `dotnet test` on a machine with no
Docker reports them as skipped rather than failed.

There are two kinds of assertion, kept deliberately apart:

- **[`expected/expectations.json`](expected/expectations.json)** - written by hand
  from the upstream DDL. Every object, column, DDL fragment and lineage edge in it
  was read out of a `CREATE` statement that `setup-databases` installs. A failure
  here is a bug.
- **`expected/baseline.json`** - recorded from a run, because nobody can enumerate
  a restored `.bak` from source. The first benchmark run against a fleet writes it
  and says so; later runs compare and report what moved. A failure here means
  "look at this and decide", and `--update-baseline` re-records it.

See [the project's README](../cli/tests/SyncSql.Samples.Benchmark.Tests/README.md)
for what each test class covers.

## Licences

The samples belong to their upstream projects and are fetched, not vendored:

- [microsoft/sql-server-samples](https://github.com/microsoft/sql-server-samples) - MIT
- [oracle-samples/db-sample-schemas](https://github.com/oracle-samples/db-sample-schemas) - MIT

The container images carry their own terms: the SQL Server image runs under the
[Developer edition licence](https://hub.docker.com/r/microsoft/mssql-server)
(non-production use), which is why `ACCEPT_EULA=Y` is set in the compose file, and
Oracle Database Free is covered by the
[Oracle Free Use Terms and Conditions](https://www.oracle.com/downloads/licenses/oracle-free-license.html).
