# Alerts

Investigate metric anomalies and orphaned references from the published catalog snapshot. These are investigation signals, not live monitoring or confirmed incidents.

## Find a finding

Choose a **Category**, then use **Search alerts** to match an object, server, database or evidence text. Filters are kept in the URL, so you can share the same view. Counts cover all matching findings; **Show more** reveals additional rows in batches of 100.

## Investigate

- **Inspect object** opens its definition and workspaces. Use **Metrics** to examine metric snapshots, **History** and **Diff** to investigate definition changes, and **Graph** for reference evidence.
- **Explore lineage** focuses the relationship graph on the affected object. Follow dependencies and consumers to understand its context.
- For an orphaned reference, inspect the caller's SQL and the unresolved target name. A renamed or dropped object is a possible explanation, not a confirmed diagnosis.

## Understand the evidence

**Metric anomalies are heuristics.** They compare the latest two metric snapshots. Row-count changes require at least 50% and 500 rows. Index fragmentation must reach at least 30% and increase by at least 20 percentage points. When a previous index reading is missing, the current fragmentation value is used for that increase check. Objects with fewer than two snapshots are not evaluated.

**Orphaned references are catalog findings.** The target did not resolve within the lookup scope: the caller's database, its server, or a server one linked server away. References into systems that are not extracted are excluded.

No alerts does not prove that the database is healthy. Missing metrics, incomplete extraction and the snapshot's age limit what can be detected. The page identifies when orphaned-reference analysis is absent.
