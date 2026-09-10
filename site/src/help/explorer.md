# Explorer

The primary way to browse the catalog: one sortable, filterable row per
extracted object. One filter bar searches object details and DDL.

## Attribute filter bar

Type to get attribute suggestions - server, database, schema, type, name,
description, DDL content - then pick an operator and enter a value or choose
one of the suggested catalog values:

- `is` / `is not` - exact match.
- `contains` - substring match.
- `is in` / `is not in` - match against a list of values.

Filters only apply once committed, not on every keystroke, so the table
stays responsive on large catalogs. Tokens are stored in the URL, so a
filtered view is a shareable link.

For a broad search, type text and press Enter. It matches server, database,
schema, type, name, description, or DDL content. Each committed filter appears
as a removable chip; multiple chips must all match.

## DDL content search

Choose **DDL content**, then **contains**, enter text and press Enter to
search *inside* each object's body: the full DDL plus the appended
`Foreign Keys`, `Check Constraints` and `Indexes` sections.

Use it for questions such as "which procedures mention `OrderStatusId`" or
"what still calls the old linked server". Combine it with other chips, such
as **Type is StoredProcedures**, to narrow the results. Existing links with
a DDL search open with that search shown as a DDL content chip.

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
