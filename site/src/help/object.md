# Object detail

Everything the pipeline knows about one object: what it looks like, what it
depends on, who can touch it and how it has changed.

## Header and quick facts

The qualified name, its type badge, and the server → database → schema trail
it came from. Where MSSQL `sys.extended_properties` descriptions exist they
are shown here (and per column below). The quick-facts row counts
dependencies, dependents and columns, and offers **Open in Lineage**, a
single-object **Export CSV**, and **Export XLSX** - the whole page as one
workbook, a worksheet per section, empty sections skipped.

## Columns

For tables and views: the full structural column list with data types, and
each column's own extended-property description where one exists.

Each row also shows how many objects are known to read that column, and
clicking that count opens the lineage for just that column: the objects that
read it, and a graph of only those. The open column is kept in the address
bar, so the view can be shared.

Only that direction is shown. A lineage edge records which of the *target's*
columns the source referenced, so "who reads this column" is exact - while
"what feeds this column" would need expression-level lineage nothing here
records, and a same-name guess would not be an answer.

## Definition

The extracted DDL. Any appended sections the extractor writes - Foreign
Keys, Check Constraints, Indexes - are rendered as their own collapsible
panels rather than buried in the script.

Code blocks use the Midnight palette for consistent SQL syntax highlighting.

## Metrics

For tables: volume, index and optimizer-statistics trends over the collected
snapshots. Metrics live in their own history store, not inside the object's
`.sql` file - a row-count change is genuinely volatile and would otherwise
show up as a DDL diff on every single run.

## Access

Who holds GRANT or DENY on this object, down to the column where a grant is
scoped that way. To go the other direction - from a principal to everything
they can reach - use the Lineage explorer's **Access** tab.

## Change history

Every mined commit that touched this object, most recent first. Click a
revision to view the definition as it stood at that point.

**Compare two revisions** switches the list into a picker: choose any two
entries - including the current definition - for a side-by-side diff. That
is the fastest way to answer "what actually changed in this proc last
Tuesday".

## Lineage

**Depends on** and **Used by**, annotated with the columns each reference
carries, plus an embedded neighborhood graph. Long lists show the first few
with the rest one click away.

An entry tagged **dynamic** was recovered from SQL built as a string at
runtime - an `OPENQUERY` body, an `EXEC` of a literal - rather than read off
the parse tree. Real, and worth showing, but a weaker claim; the graph draws
those edges dashed for the same reason.

Hub objects are handled deliberately: past a threshold the lists summarize
rather than rendering thousands of rows, and the full set is available as a
CSV export. **Open in Lineage** opens the same neighborhood in the full
graph explorer, seeded with a filter token for this object.

## System objects referenced

Objects the database engine provides rather than anything the pipeline
extracts - `sp_executesql`, `sys.*`, Oracle's `DBMS_*`. They resolve to
nothing in the catalog, but they are not missing and never were, so they are
listed here rather than counted as orphaned references.

## Orphaned reference warning

If this object's own DDL refers to something the lookup cannot resolve -
within its database, its server, or one linked server away - the warning
appears here. It usually means a renamed or dropped target that this caller
was never updated for.

Deliberately not flagged, because none of them means "the target is missing":
system objects (above), temp tables, CTE names and the FROM-clause aliases a
multi-table DELETE/UPDATE targets (all names created by this very script),
anything merely ambiguous or outside what gets extracted, and references
recovered from dynamically-built SQL, whose text may depend on values only
known at runtime.
