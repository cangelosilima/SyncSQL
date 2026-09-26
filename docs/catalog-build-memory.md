# Catalog progress and memory investigation

`syncsql catalog build` now uses the same synchronized terminal as `sync`. It shows the current phase, completed/total objects or partitions, current object, node/edge counts, elapsed and phase time, resident RAM, process peak RAM, and an estimate of managed heap usage. Percentages apply to the current phase. File discovery and Git history have unknown totals rather than an estimated percentage. Warnings remain visible above the live display.

Redirected output logs phase changes and a heartbeat every ten seconds, including while one object is still parsing. Completion, failure and cancellation preserve their status and restore the terminal cursor. Memory sampling never forces garbage collection. RAM is the operating system's working set; managed memory is `GC.GetTotalMemory(false)` and includes garbage awaiting collection.

## Findings and changes

The existing builder already discards each object's raw lineage analysis after resolving its references. The earlier Oracle change also stopped generated ANTLR prediction caches from accumulating for the entire process, by creating fresh caches per parse. That fixed lifetime growth but repeatedly paid the substantial cost of constructing a cold PL/SQL prediction cache.

A focused probe of the benchmark's select, join and nested-query statements attributed roughly 200–300 MiB of allocations per cold parse to the parser, versus about 1 MiB to tokenization after initialization. Trying SLL alone did not materially reduce those cold allocations. Reusing warmed prediction tables was the larger improvement.

The analyzer now leases one reusable cache per analyzer instance. Concurrent calls receive independent caches, and only one idle cache is retained. The cache stores prediction tables and contexts; it never stores parser instances, tokens, input text or parse trees. It is discarded after a parsing failure, or once any retention limit is reached:

- 16 parsed scripts, including nested dynamic SQL, or 1,048,576 cumulative input characters.
- More than 2,048 DFA states, 250,000 configurations, or 16,384 shared prediction contexts.

These limits are checked between top-level objects. They constrain cache retention, not the peak memory needed to parse one complex object. Parsing uses the original LL prediction mode and normal error recovery. The experimental SLL-first pass and whole-input retry were removed after the comparison below showed no material benefit with bounded cache reuse. The existing grammar and lineage visitor remain in use.

## Measurements

Windows x64, .NET 10.0.11, SDK 10.0.203, BenchmarkDotNet 0.15.8. The same existing workloads validate object counts, dependencies, column tags and orphan counts. Baseline reports are in `TestResults/performance`; the complete eight-case run with the original LL mode restored is in `TestResults/catalog-bounded-cache-ll`.

| Oracle workload | Previous allocation | New allocation | Previous mean | New mean |
| --- | ---: | ---: | ---: | ---: |
| Build 16 objects | 3,808 MiB | 364 MiB | 2.54 s | 0.241 s |
| Build 64 objects | 15,970 MiB | 1,444 MiB | 10.76 s | 0.797 s |
| Parse 16 statements, including dynamic SQL | 3,978 MiB | 455 MiB | 2.49 s | 0.259 s |

Catalog allocation fell about 90–91%. SQL Server allocation stayed at 1.11/4.43 MiB for the two catalog sizes. Synthetic retained-analysis growth remained below 1 MiB at both 64 and 256 objects. These synthetic retention checks test the builder, not Oracle cache retention; separate weak-reference tests verify cache expiration and release of parser/input objects, and concurrency tests verify independent leases.

These are cumulative managed allocations per operation, **not peak resident RAM**. The three-iteration timing samples have wide confidence intervals; they demonstrate a large improvement but are not precise production throughput predictions. The reported production 5 GB peak has not been reproduced with its original input.

## SLL-first rollback comparison

Both runs use identical bounded-cache limits and the same benchmark workloads. The SLL-first results remain in `TestResults/catalog-bounded-cache`; the LL rerun is in `TestResults/catalog-bounded-cache-ll`. No performance budgets were relaxed for the rerun. All eight benchmarks and both retained-analysis budgets passed.

| Oracle workload | SLL-first allocation | Restored LL allocation | SLL-first mean | Restored LL mean |
| --- | ---: | ---: | ---: | ---: |
| Build 16 objects | 365.63 MiB | 364.30 MiB | 251.51 ms | 240.69 ms |
| Build 64 objects | 1,450.05 MiB | 1,444.36 MiB | 808.15 ms | 797.15 ms |
| Parse 16 statements | 461.03 MiB | 455.11 MiB | 271.96 ms | 258.77 ms |

Restoring LL preserved the allocation reduction and slightly lowered allocation in these workloads. The small timing differences are within measurement noise. The SLL-first path is therefore removed; the bounded cache remains. Regression tests cover unchanged lineage with a warmed cache, normal error recovery in one parsing pass, cache expiration, and concurrent calls.

## Remaining investigation

The builder retains the complete current DDL, metadata, metrics and requested history in the final catalog. That memory still grows with the input size. A single large PL/SQL object can also create a large token stream, parse tree or prediction cache before a retention limit is checked. Publication creates temporary JSON and compression buffers; a single object can exceed the nominal 2 MiB partition target.

The new phase/current-object and memory readings distinguish these cases. A peak during one parse calls for profiling that DDL and considering statement-level parsing or an isolated parser worker with a defined failure policy. Growth during scanning/history calls for lazy or disk-backed DDL/history storage. Peaks during publication call for streaming serialization, hashing and compression. These are follow-up changes; this patch does not impose a hard process-memory cap or skip large objects.
