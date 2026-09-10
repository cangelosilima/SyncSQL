# AdventureWorks (OLTP + DW)

The classic Microsoft sample databases. Not a demo of its own, but the base every AdventureWorks-based demo in samples/demos reads from.

- **Upstream**: [`samples/databases/adventure-works`](https://github.com/microsoft/sql-server-samples/tree/master/samples/databases/adventure-works) in [`sql-server-samples`](https://github.com/microsoft/sql-server-samples)
- **Engine**: Microsoft SQL Server
- **Databases**: `AdventureWorks2022`, `AdventureWorksDW2022`
- **Tier**: `standard` - provisioned by default.

## What gets installed

Restored from the official backups published on the upstream repository's releases:

| Backup | Restored as | Download |
|--------|-------------|----------|
| `AdventureWorks2022.bak` | `AdventureWorks2022` | [200 MB](https://github.com/Microsoft/sql-server-samples/releases/download/adventureworks/AdventureWorks2022.bak) |
| `AdventureWorksDW2022.bak` | `AdventureWorksDW2022` | [97 MB](https://github.com/Microsoft/sql-server-samples/releases/download/adventureworks/AdventureWorksDW2022.bak) |

`setup-databases` reads the logical file names out of each backup with `RESTORE FILELISTONLY`
and builds the `MOVE` clauses from them, so the restore does not depend on the paths the
backup was taken from.

## In the catalog

Extracted objects land under:

```
samples/output/SAMPLES-MSSQL/AdventureWorks2022/<schema>/<type>/<object>.sql
samples/output/SAMPLES-MSSQL/AdventureWorksDW2022/<schema>/<type>/<object>.sql
```

## Licence

The upstream material is MIT-licensed - see [the upstream licence](https://github.com/microsoft/sql-server-samples/blob/master/license.txt).
Nothing from it is vendored into this repository; `setup-databases` fetches it at provisioning time.
