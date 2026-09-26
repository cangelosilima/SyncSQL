# CLI performance benchmarks

`SyncSql.Benchmarks` runs BenchmarkDotNet 0.15.8 against the production catalog builder and lineage analyzers. It is separate from the Docker-backed extraction tests in `SyncSql.Samples.Benchmark.Tests`; no database, credentials, or Docker daemon is needed.

From the repository root, run the complete quality gate:

```powershell
pwsh ./scripts/Run-CliBenchmarks.ps1
```

This builds Release, runs all eight benchmark cases in isolated BenchmarkDotNet processes, then enforces [budgets.json](budgets.json). Reports go to `TestResults/performance/`. For another run, choose a fresh directory:

```powershell
pwsh ./scripts/Run-CliBenchmarks.ps1 -ArtifactsPath TestResults/performance-next
```

The runner rejects nonempty output directories so old reports cannot satisfy a failed run. BenchmarkDotNet failures also return a nonzero exit code. The gate rejects missing, duplicate, unexpected, malformed, or incomplete results, fewer than three measured samples, and exceeded budgets. It writes `budget-summary.md` alongside the full JSON, Markdown, HTML, CSV, logs, and retained-memory reports.

For an exploratory run of selected cases (without enforcing the complete gate):

```powershell
dotnet run --project cli/benchmarks/SyncSql.Benchmarks --configuration Release -- --filter '*ParserBenchmarks*'
```

Exploratory artifacts default to `BenchmarkDotNet.Artifacts/`. Set `SYNCSQL_BENCHMARK_ARTIFACTS` to an absolute directory to change it. BenchmarkDotNet's standard filters and other command-line options remain available. The normal quality job uses one launch, one warmup, three measured iterations, and one invocation per iteration; it keeps outliers. Its coarse time limits detect major slowdowns, rather than small statistically significant changes on shared runners.

## Workloads and measurements

| Workload | Cases | What it exercises |
| --- | --- | --- |
| `CatalogBenchmarks.Build` | 16 and 64 objects, SQL Server and Oracle | Reading real exported files, real grammar parsing, resolving dependencies and column tags. Files are generated outside the measured method. |
| `ParserBenchmarks.AnalyzeBatch` | SQL Server and Oracle | Sixteen statements with joins, nested queries, and dynamic SQL. Each result must retain expected dependencies; supported column bindings are checked too. |
| `SyntheticRetentionBenchmarks.BuildAndSampleRetention` | 64 and 256 objects | The former `.cache/catalog-memory-probe`, now executed by BenchmarkDotNet. A synthetic analyzer emits 8,193 column references per object to stress discarded analysis. |

Every build checks the expected node count, edge count, column tags, and absence of orphans. Performance cannot improve by silently losing lineage. The synthetic workload uses the current production builder; the old copied builder and custom stopwatch harness are retired.

BenchmarkDotNet's `MemoryDiagnoser` reports **total managed allocations per operation** and GC activity, as described in its [diagnoser documentation](https://benchmarkdotnet.org/articles/configs/diagnosers.html). This is different from retained memory and peak process RAM.

The synthetic benchmark samples `GC.GetTotalMemory(true)` before analysis of the first object, every 32 objects, and the final object. Its extra report records the maximum growth above the first sample across completed builds. Each case must stay below **16 MiB of retained analysis growth**. Forced collections are intentional here and are included in this diagnostic workload's timing; the real catalog and parser benchmarks do not force collections. These samples do not capture transient peaks, native allocations, or the whole CLI's resident memory. Keep the xUnit weak-reference regression tests as well: they verify object/cache lifetimes directly.

The initial Windows x64 run measured roughly 1 MiB of retained analysis growth at both synthetic sizes. It also exposed substantial Oracle parser allocation churn: 3,808 MiB allocated for a 16-object catalog and 15,970 MiB for 64 objects. Bounded prediction-cache reuse with the original LL parser reduced these to 364 MiB and 1,444 MiB on the same machine, with build means of 241 ms and 797 ms. The parser batch dropped from 3,978 MiB to 455 MiB. The SLL-first experiment was removed after a direct benchmark comparison showed no material benefit. Oracle allocation budgets retain at least twice the measured headroom while rejecting a return to the previous churn; they were unchanged for the LL rerun. See [the investigation](../../docs/catalog-build-memory.md) for the comparison, cache limits, progress output, and remaining sources of peak RAM.

## CI and budget changes

The `CLI performance budgets` job in `.github/workflows/quality.yml` runs on pull requests, merge groups, main pushes, manual runs, and the existing weekly schedule. It is a required dependency of the existing `Quality gate`, publishes a job summary, and uploads `quality-cli-performance` artifacts even after failure. Repository branch protection must require `Quality gate` to block merging; workflow changes alone do not change repository settings.

The checked-in budgets are absolute limits calibrated on the initial Windows run, with allocation headroom and generous runtime limits for Linux CI. Review the first hosted-run reports when calibrating further. Do not automatically regenerate budgets from the same run being evaluated. To update a budget, inspect the full report, explain the changed workload or accepted performance cost, and review the JSON change alongside the code.

Test the gate's rejection paths without running benchmarks:

```powershell
pwsh ./scripts/Test-CliBenchmarkGate.ps1
```
