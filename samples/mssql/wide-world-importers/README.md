# Wide World Importers (OLTP + DW)

The modern Microsoft sample databases. Base for the sql-graph and automatic-tuning demos.

- **Upstream**: [`samples/databases/wide-world-importers`](https://github.com/microsoft/sql-server-samples/tree/master/samples/databases/wide-world-importers) in [`sql-server-samples`](https://github.com/microsoft/sql-server-samples)
- **Engine**: Microsoft SQL Server
- **Databases**: `WideWorldImporters`, `WideWorldImportersDW`
- **Tier**: `standard` - provisioned by default.

## What gets installed

Restored from the official backups published on the upstream repository's releases:

| Backup | Restored as | Download |
|--------|-------------|----------|
| `WideWorldImporters-Full.bak` | `WideWorldImporters` | [121 MB](https://github.com/Microsoft/sql-server-samples/releases/download/wide-world-importers-v1.0/WideWorldImporters-Full.bak) |
| `WideWorldImportersDW-Full.bak` | `WideWorldImportersDW` | [48 MB](https://github.com/Microsoft/sql-server-samples/releases/download/wide-world-importers-v1.0/WideWorldImportersDW-Full.bak) |

`setup-databases` reads the logical file names out of each backup with `RESTORE FILELISTONLY`
and builds the `MOVE` clauses from them, so the restore does not depend on the paths the
backup was taken from.

## In the catalog

Extracted objects land under:

```
samples/output/SAMPLES-MSSQL/WideWorldImporters/<schema>/<type>/<object>.sql
samples/output/SAMPLES-MSSQL/WideWorldImportersDW/<schema>/<type>/<object>.sql
```

## Licence

The upstream material is MIT-licensed - see [the upstream licence](https://github.com/microsoft/sql-server-samples/blob/master/license.txt).
Nothing from it is vendored into this repository; `setup-databases` fetches it at provisioning time.
