# Automatic tuning

The dbo.report procedure the automatic-tuning workload regresses and the engine then force-plans. The Windows Forms workload driver is not installed.

- **Upstream**: [`samples/demos/automatic-tuning`](https://github.com/microsoft/sql-server-samples/tree/master/samples/demos/automatic-tuning) in [`sql-server-samples`](https://github.com/microsoft/sql-server-samples)
- **Engine**: Microsoft SQL Server
- **Databases**: `WideWorldImporters`
- **Requires**: [`wide-world-importers`](../wide-world-importers/)
- **Tier**: `standard` - provisioned by default.

## What gets installed

Into `WideWorldImporters`, in this order:

1. [`install/00-prepare.sql`](install/00-prepare.sql) - a SyncSQL prelude, not upstream material. Read it; it says what it fixes up.
1. `WideWorldImporters/dbo/Stored Procedures/report.sql` - from [upstream](https://github.com/microsoft/sql-server-samples/blob/master/samples/demos/automatic-tuning/WideWorldImporters/dbo/Stored%20Procedures/report.sql)

### Not installed

| Upstream path | Why |
|---------------|-----|
| `DemoWorkload` | A .NET Framework Windows Forms workload driver - not a database object. |

## In the catalog

Extracted objects land under:

```
samples/output/SAMPLES-MSSQL/WideWorldImporters/<schema>/<type>/<object>.sql
```

## Licence

The upstream material is MIT-licensed - see [the upstream licence](https://github.com/microsoft/sql-server-samples/blob/master/license.txt).
Nothing from it is vendored into this repository; `setup-databases` fetches it at provisioning time.
