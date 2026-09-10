# Extended Events

The same warnings as the Showplan demo, observed through Extended Events sessions instead of showplan XML.

- **Upstream**: [`samples/demos/xEvents`](https://github.com/microsoft/sql-server-samples/tree/master/samples/demos/xEvents) in [`sql-server-samples`](https://github.com/microsoft/sql-server-samples)
- **Engine**: Microsoft SQL Server
- **Databases**: `memgrants`
- **Requires**: [`showplan`](../showplan/)
- **Tier**: `heavy` - skipped unless you pass `--tier heavy` (or `--tier all`).

## What gets installed

Into `memgrants`, in this order:

1. `MemoryGrant_XE_Setup.sql` - from [upstream](https://github.com/microsoft/sql-server-samples/blob/master/samples/demos/xEvents/MemoryGrant_XE_Setup.sql)

### Not installed

| Upstream path | Why |
|---------------|-----|
| `HashWarning.sql` | Query-only demo script - creates no objects. |
| `SortWarning.sql` | Query-only demo script - creates no objects. |
| `MemoryGrant_XE.sql` | Creates a server-level Extended Events session, not a database object SyncSQL extracts. |
| `PerfStats_XE_Demo.sql` | Creates a server-level Extended Events session, not a database object SyncSQL extracts. |

> Identical 10,000,000-row setup to the Showplan demo, hence tier 'heavy'.

## In the catalog

Extracted objects land under:

```
samples/output/SAMPLES-MSSQL/memgrants/<schema>/<type>/<object>.sql
```

## Licence

The upstream material is MIT-licensed - see [the upstream licence](https://github.com/microsoft/sql-server-samples/blob/master/license.txt).
Nothing from it is vendored into this repository; `setup-databases` fetches it at provisioning time.
