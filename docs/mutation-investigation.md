# CLI mutation investigation — 2026-09-26

The original 100% CLI smoke result covered only `NameFilter.cs`. Full runs of
`SyncSql.Core` and `SyncSql.Cli` found substantially more gaps. The regression tests
added during this investigation protect existing behavior. The follow-up also
refactors scheduling and deserialization to make those behaviors testable without
changing their public contracts.

## Initial investigation results

| Scope | Before | After targeted regression tests |
| --- | ---: | ---: |
| Entire Core project | 80.56% | 86.67% |
| Core configuration and credentials | 82.60% | 91.40% |
| Core process runner and progress aggregator | 60.71% | 85.71% |
| Catalog and lint commands | 56.59% | 63.57% |

The initial complete Core run scored 80.56%: 636 killed, 23 timed out, 150 survived,
and nine had no coverage. The initial complete command-project run scored 69.23%:
424 killed, eight timed out, and 192 survived. These are separate projects, not an
aggregate score for the entire CLI solution. Catalog, both extraction projects,
and both lineage projects have not been mutation-tested in this investigation.

That complete Core verification recorded 687 killed, 22 timed out, 103 survived, and
six uncovered mutations, with the same 119 compile-error exclusions. The final
command verification was scoped to `CatalogCommand.cs` and `LintCommand.cs`:
82 killed and 47 survived, versus 73 killed and 56 survived before. The whole
command-project score was not rerun after adding its five regression cases.

## Behavioral gaps addressed

- Credential resolution must preserve the higher-priority half while filling the
  missing half from a lower-priority provider, and stop reading providers once both
  halves are available. Empty values must permit fallback. Credential files accept
  case-insensitive property names and trailing commas.
- Every configuration filter location must reject invalid include and exclude
  expressions with a useful location in the error. An expression present in both
  lists must still be validated. Zero discovery depth remains valid.
- Oracle identities retain the service and port, normalize aliases, and clean tags.
  Valid boundary SQL Server ports still receive the configured DNS suffix.
- Ownerless link overrides apply to owned links when no owner-specific match exists,
  retaining exact evidence fields and their configuration source. Default object
  name exclusions must be inherited.
- Process failures retain meaningful stderr. Independent extraction jobs contribute
  count deltas without double-counting repeated progress; parent updates retain the
  accumulated count.
- Catalog commands forward multiple input roots, metrics/repository paths,
  `--path-prefix` and publication options. A single input root
  supplies the default publication directory. Lint must discover invalid SQL in
  nested directories through either `--path` or `--output-root`.

There are 20 additional Core cases and five additional command-project cases, plus
a stronger assertion in the existing failed-process test. All 302 Core tests and
100 command-project tests pass.

## Findings from the initial investigation

Stryker 5.0.0 excludes mutations that cannot compile. The full runs excluded 119 Core
mutations and 390 command-project mutations. In particular, definite-assignment
errors caused Stryker's recovery to remove mutations from the configuration and
credential `LoadAsync` methods and `SyncCommand.Build`. An invalid string arithmetic
mutation also affected `ExtractionProgressDisplay.StartCatalog`. These exclusions
are not killed mutations and must not be interpreted as verified behavior.

Remaining survivors warrant case-by-case review. Priorities include extracted-file
serialization, linked-server discovery, scheduler fairness/cancellation, and command
failure/progress handling. Other survivors change help/log text, platform window
behavior, or equivalent expressions such as `First` versus `FirstOrDefault` on a
collection guaranteed to be nonempty. Do not suppress them wholesale or claim 100%
protection based on a score that excludes uncompilable mutations.

The scheduler mutation removing `Token.ThrowIfCancellationRequested()` was killed
in the initial run but survived the final run. That exposed timing-sensitive
test detection; see the follow-up below.

## Follow-up improvements

The scheduler now accepts an internal `TaskScheduler` while its public constructor
retains the default thread-pool behavior and `DenyChildAttach` semantics. Tests can
hold a dispatched job before execution, cancel its batch, then release it. They
assert that work is never invoked, cancellation is returned, and the scheduler can
be reused. A controlled dispatcher also verifies rotation between equally loaded
engines without relying on sleeps or thread timing.

Configuration and credential deserialization now return from small helpers instead
of assigning locals across a `try`/`catch`. Sync queues each work item inside the
successful credential-resolution branch. Terminal log construction uses string
interpolation. These preserve normal behavior while allowing Stryker to instrument
methods it previously dropped after invalid mutations.

Additional contracts cover human-readable export headers, schema-less names,
column-level DENY grants, column-only descriptions, irrelevant extended properties,
adjacent section boundaries, empty inputs, safe relative filenames, discovery depth,
mixed remote-login mappings, registered ports/suffixes, unique discovered names,
and handled catalog failures restoring the terminal with a failed status.

### Follow-up measurements

The complete Core rerun improved from **86.67% to 91.60%**: 741 killed, 22 timed
out, 67 survived, and three uncovered mutations. Compile-error exclusions fell
from 119 to 91. The cancellation-guard removal is killed specifically by
`CancellationAfterDispatchButBeforeExecutionNeverInvokesWork`, which controls
dispatch instead of depending on thread timing.

The targeted CLI rerun covers `SyncCommand.cs`, `CatalogCommand.cs`, and
`ExtractionProgressDisplay.cs`: **56.32%**, with 242 killed, three timed out, and
190 survivors. This is not comparable to the earlier whole-project score, both
because the scope differs and because previously excluded code now participates.

| File | Previous compile errors | Follow-up compile errors | Previous survivors | Follow-up survivors |
| --- | ---: | ---: | ---: | ---: |
| Core configuration loader | 26 | 12 | 5 | 7 |
| Credentials file provider | 17 | 3 | 0 | 2 |
| Sync command | 300 | 99 | 8 | 119 |
| Extraction progress display | 23 | 15 | 45 | 48 |
| Catalog command | 10 | 10 | 36 | 23 |
| Linked-server follow-up planner | 20 | 20 | 13 | 8 |

Neither follow-up run emitted the prior whole-method compiler recovery warnings.
Ordinary uncompilable mutations remain excluded. Newly testable sync paths expose
remaining gaps around server-selection overrides, discovery, and command failure
handling; help/log text also contributes survivors. These require individual
review rather than blanket exclusions.

The full solution Release verification passed **1,018 tests**, with **21 skipped**
and no failures, including 328 Core and 115 command-project tests. The complete
Core mutation run captured 325 tests before three final serialization edge cases
were added; those cases are included in the Release verification and the focused
serialization rerun.

The focused serialization rerun scored **97.83%** (179 killed, one timed out,
four survived), up from 82.07% in the previous full Core verification. It retains
28 compile-error exclusions. Remaining survivors affect grant output and parser
control flow and are recorded for individual review.

Local HTML/JSON reports are under `TestResults/mutation-investigation/` (initial
full runs and focused reruns) and `TestResults/mutation-verification/` (full Core
verification), with follow-up reports under `TestResults/mutation-improvements/`.
They are ignored build artifacts. See [mutation testing](mutation-testing.md)
for pinned tools, commands, CI scheduling, and the scoped merge-gate thresholds.
