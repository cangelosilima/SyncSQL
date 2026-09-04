# Object detail

Everything the pipeline knows about one object: what it looks like, what it
depends on, who can touch it and how it has changed.

## Header and quick facts

The qualified name, its type badge, and the server → database → schema trail
it came from. Where MSSQL `sys.extended_properties` descriptions exist they
are shown here (and per column below). The quick-facts row counts
dependencies, dependents and columns, and offers **Open in Lineage** plus a
single-object **Export CSV**.

## Columns

For tables and views: the full structural column list with data types, and
each column's own extended-property description where one exists.

## Definition

The extracted DDL. Any appended sections the extractor writes - Foreign
Keys, Check Constraints, Indexes - are rendered as their own collapsible
panels rather than buried in the script.

Code blocks always use the Midnight palette regardless of the site theme, so
SQL keeps one consistent look in both light and dark.

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

Hub objects are handled deliberately: past a threshold the lists summarize
rather than rendering thousands of rows, and the full set is available as a
CSV export. **Open in Lineage** opens the same neighborhood in the full
graph explorer, seeded with a filter token for this object.

## Orphaned reference warning

If this object's own DDL refers to something the lookup cannot resolve -
within its database, its server, or one linked server away - the warning
appears here. It usually means a renamed or dropped target that this caller
was never updated for.
