# SyncSql.Samples.Benchmark.Tests

The [heterogeneous lineage scenario](../../../samples/scenarios/heterogeneous-lineage/README.md)
also lives in this project. Its `HeterogeneousLineageTests` always run; only
`HeterogeneousLiveTests` needs `SYNCSQL_HETEROGENEOUS=1`. It has its own three-server
Docker fleet, 48 users and an independent expected catalog, and runs through the
real CLI after provisioning. Use the scenario runner to set credentials and start
the containers. The older upstream sample fleet below retains its own opt-in flag.

An end-to-end benchmark: extract every database in
[`samples/`](../../../samples), build the catalog from what came out, and assert
the result.

Unlike the other test projects here, this one needs a live fleet - the two
containers `samples/scripts/setup-databases.sh` brings up. So every test in it
carries `[SampleFleetFact]` instead of `[Fact]`, which sets `Skip` unless
`SYNCSQL_SAMPLES=1`. A plain `dotnet test cli/SyncSql.slnx` on a machine with no
Docker reports these as skipped, with a message saying how to run them.

## Running it

```sh
samples/scripts/setup-databases.sh    # once, to build the fleet
samples/scripts/run-benchmark.sh      # extract, then assert
```

Or by hand, from an already-provisioned fleet:

```sh
SYNCSQL_SAMPLES=1 dotnet test cli/tests/SyncSql.Samples.Benchmark.Tests
```

| Variable | Effect |
|----------|--------|
| `SYNCSQL_SAMPLES` | `1` to run the benchmark at all. Anything else skips it. |
| `SYNCSQL_SAMPLES_REUSE` | `1` to assert against the existing `samples/output` instead of re-extracting. Much faster while iterating on assertions. |
| `SYNCSQL_SAMPLES_OUTPUT` | Where the extraction goes. Default: `samples/output`. |
| `SYNCSQL_SAMPLES_UPDATE_BASELINE` | `1` to re-record `samples/expected/baseline.json` from this run. |
| `SYNCSQL_CLI` | A published `SyncSql.Cli.dll` (or executable) to run instead of building from source. `run-benchmark.sh` sets this. |

## How it is put together

`SampleBenchmarkFixture` is a collection fixture, so the extraction happens once
and all four test classes read the same catalog. It drives the real CLI as a child
process - `validate-config`, `sync`, `metrics update`, `catalog build` - rather
than calling into it, because that is how a pipeline invokes it: argument parsing,
exit codes and file output all get exercised as they ship.

| Class | Asserts |
|-------|---------|
| `PipelineTests` | The run produced what a pipeline would publish: a catalog, a metrics history, objects for both engines, and no catalog node pointing at a file that is not there. Failures here carry the CLI's own output, so start here when the benchmark goes red. |
| `ExtractedObjectTests` | The file tree. Every expected object has its own `.sql` file at the path `ExtractedObjectFile.RelativePath` computes, every file round-trips through `Parse`, expected DDL fragments and columns survived, and the per-type object counts match the upstream schemas exactly. |
| `CatalogTests` | The catalog. Every expected object is a node, every expected **lineage edge** was inferred, type counts agree with the nodes they summarize, no edge dangles, and no schema-qualified orphaned reference points at something the catalog actually has. |
| `BaselineTests` | The recorded totals, run over run. |

## The two kinds of expectation

[`samples/expected/expectations.json`](../../../samples/expected/expectations.json)
is **hand-written from the upstream DDL**. `HR.EMPLOYEES` is in it because
`hr_create.sql` creates it; the six edges out of `HR.EMP_DETAILS_VIEW` are in it
because its `SELECT` names six tables. Nothing in that file was recorded from a
run, which is what makes a failure there meaningful: it says the extraction or the
lineage analysis stopped producing something it is supposed to produce.

`samples/expected/baseline.json` is **recorded**, because the numbers in it cannot
be derived from source - AdventureWorks arrives as a binary backup, and how many
lineage edges the whole fleet yields is an emergent property of the analyzers. The
first run against a fleet writes the file and says so in its output; later runs
compare and report what moved. That is a prompt to look, not a verdict:
`--update-baseline` re-records it once you have decided the change is right.

Keeping the two apart is the point. A single "golden output" file would make every
real regression look like every intentional improvement.

## Adding a sample to the benchmark

1. Add it to [`samples/samples.json`](../../../samples/samples.json) with a
   `provision` block, and write its folder README.
2. Add an `ExpectedGroup` for it in `expectations.json`, listing the objects its
   install script creates and any lineage edge its DDL implies. Read them out of
   the DDL - do not copy them from a run.
3. Re-record the baseline: `samples/scripts/run-benchmark.sh --update-baseline`.
