# Showplan warnings

Hash, sort and memory-grant warnings in actual execution plans, against a purpose-built 'memgrants' database.

- **Upstream**: [`samples/demos/Showplan`](https://github.com/microsoft/sql-server-samples/tree/master/samples/demos/Showplan) in [`sql-server-samples`](https://github.com/microsoft/sql-server-samples)
- **Engine**: Microsoft SQL Server
- **Databases**: `memgrants`
- **Tier**: `heavy` - skipped unless you pass `--tier heavy` (or `--tier all`).

## What gets installed

Into `memgrants`, in this order:

1. `MemoryGrant_Warning_Setup.sql` - from [upstream](https://github.com/microsoft/sql-server-samples/blob/master/samples/demos/Showplan/MemoryGrant_Warning_Setup.sql)

### Not installed

| Upstream path | Why |
|---------------|-----|
| `HashWarning.sql` | Query-only demo script - creates no objects. |
| `SortWarning.sql` | Query-only demo script - creates no objects. |
| `MemoryGrant_Warning.sql` | Query-only demo script - creates no objects. |

> The setup script inserts 10,000,000 rows one at a time in a WHILE loop, which takes hours in a container. That is why this sample is tier 'heavy' and is skipped unless you ask for it.

## In the catalog

Extracted objects land under:

```
samples/output/SAMPLES-MSSQL/memgrants/<schema>/<type>/<object>.sql
```

## Licence

The upstream material is MIT-licensed - see [the upstream licence](https://github.com/microsoft/sql-server-samples/blob/master/license.txt).
Nothing from it is vendored into this repository; `setup-databases` fetches it at provisioning time.
