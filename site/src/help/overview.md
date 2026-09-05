# Overview

The landing page answers "what does the catalog look like right now, and is
anything obviously wrong?". Everything on it is computed from
`data/catalog.json`, the static snapshot the `analyze-catalog` CI job
publishes alongside the site - there is no live database connection behind
any of these numbers.

## Quick stats

- **Total objects** - every extracted object across every server and
  database in the snapshot.
- **Commits mined** - how many commits the pipeline read out of the object
  repository. It is bounded by a configurable commit window, so it is not
  the repository's whole history.
- **Lineage edges** - references resolved by the engine parsers (T-SQL
  ScriptDom, ANTLR4 PL/SQL), not text matches.
- **Last change** - the most recently changed object in the mined window.

## Metric anomalies

Tables whose latest metrics snapshot swung sharply against the previous one:
a row-count jump or drop, or an index fragmentation spike. Metrics are
collected per run and stored as history outside the object's own `.sql`
file, so a volume change never shows up as a spurious DDL diff. An empty
panel means nothing crossed the threshold in the latest snapshot - not that
metrics are missing.

## Orphaned references

References that resolve to nothing the lookup can reach: the object's own
database, the rest of its server, or a server one linked server away.
These are usually a renamed or dropped target whose caller was never
updated. The panel's value is in what it leaves out, so several things are
deliberately not counted: a reference into a database nobody extracts, a
system object the engine provides (`sp_executesql`, `sys.*`), a temp table or
CTE the script creates for itself, an ambiguous name, and anything recovered
from SQL built as a string at runtime. That keeps it quiet on
partially-extracted estates and on ordinary, correct code.

Only the first 10 are listed; the count in the heading is the real total.

## The panels

- **Change activity** - one row per object type, one cell per week over the
  mined history. It doubles as the object-count-by-type breakdown.
- **Latest changes** - the 10 most recently changed objects.
- **Most referenced tables** - *direct* counts objects pointing straight at
  the table; *indirect* adds transitive dependents, and stops one hop past
  a linked-server boundary.
- **Most changed objects** - change count over the whole mined window.
- **Commonly changed together** - object pairs that keep landing in the same
  commit. A strong pair is often an undocumented coupling worth knowing
  about before you touch either side.

If the change-driven panels are empty, `analyze-catalog` ran without
`-RepoRoot` and mined no git history for that run.
