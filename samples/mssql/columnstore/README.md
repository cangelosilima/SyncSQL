# Rowstore vs columnstore

Side-by-side IO comparison of a page-compressed rowstore table against a clustered columnstore index, over the extended AdventureWorksDW tables.

- **Upstream**: [`samples/demos/columnstore`](https://github.com/microsoft/sql-server-samples/tree/master/samples/demos/columnstore) in [`sql-server-samples`](https://github.com/microsoft/sql-server-samples)
- **Engine**: Microsoft SQL Server
- **Databases**: `AdventureWorksDW2016`
- **Tier**: `heavy` - skipped unless you pass `--tier heavy` (or `--tier all`).

## What gets installed

Restored from the official backups published on the upstream repository's releases:

| Backup | Restored as | Download |
|--------|-------------|----------|
| `AdventureWorksDW2016_EXT.bak` | `AdventureWorksDW2016` | [883 MB](https://github.com/Microsoft/sql-server-samples/releases/download/adventureworks/AdventureWorksDW2016_EXT.bak) |

`setup-databases` reads the logical file names out of each backup with `RESTORE FILELISTONLY`
and builds the `MOVE` clauses from them, so the restore does not depend on the paths the
backup was taken from.

> The demo query reads FactResellerSalesXL_PageCompressed and FactResellerSalesXL_CCI, which only exist in the 'EXT' backup - an 883 MB download, hence tier 'heavy'. The .sql file itself is query-only and creates no objects.

## In the catalog

Extracted objects land under:

```
samples/output/SAMPLES-MSSQL/AdventureWorksDW2016/<schema>/<type>/<object>.sql
```

## Licence

The upstream material is MIT-licensed - see [the upstream licence](https://github.com/microsoft/sql-server-samples/blob/master/license.txt).
Nothing from it is vendored into this repository; `setup-databases` fetches it at provisioning time.
