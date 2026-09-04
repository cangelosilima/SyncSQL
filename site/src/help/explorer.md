# Explorer

The primary way to browse the catalog: one sortable, filterable row per
extracted object. Two independent search inputs sit above the table, and
they do different jobs.

## Attribute filter bar

Type to get attribute suggestions - server, database, schema, type, name,
description - then pick an operator and a value drawn from the catalog
itself:

- `is` / `is not` - exact match.
- `contains` - substring match.
- `is in` / `is not in` - match against a list of values.

Filters only apply once committed, not on every keystroke, so the table
stays responsive on large catalogs. Tokens are stored in the URL, so a
filtered view is a shareable link.

## DDL content search

The second box searches *inside* each object's body: the full DDL plus the
appended `Foreign Keys`, `Check Constraints` and `Indexes` sections. It
filters as you type (debounced), with no operator to choose.

Use it for questions the attribute filter can't answer - "which procedures
mention `OrderStatusId`", "what still calls the old linked server". The two
inputs combine: attribute filters narrow first, then the content search runs
over what is left.

## Sorting and the row cap

Every column header except **Description** sorts; click again to reverse.
The table renders at most 500 rows - narrow the filter to see the rest. The
count line above the filter bar always reports the true number of matches.

## Export CSV

**Export CSV** downloads *every* matching object, not just the rows on
screen, using the filter exactly as it stands. The file name carries a
timestamp so repeated exports don't overwrite one another.

## Getting to detail

Click an object name to open its detail page: DDL, columns, metrics,
access, change history and its lineage neighborhood.
