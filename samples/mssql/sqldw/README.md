# SQL Data Warehouse free trial lab

PolyBase external data sources, external file formats and external tables over Azure Blob Storage.

- **Upstream**: [`samples/demos/SQLDW`](https://github.com/microsoft/sql-server-samples/tree/master/samples/demos/SQLDW) in [`sql-server-samples`](https://github.com/microsoft/sql-server-samples)
- **Engine**: Microsoft SQL Server
- **Tier**: `unsupported` - never provisioned; see *Why it is not installed* below.

## Why it is not installed

Targets a dedicated Azure Synapse SQL pool: the scripts use CREATE EXTERNAL DATA SOURCE over Azure Blob Storage, CTAS with distribution hints, and Synapse-only resource classes. None of it runs on a SQL Server container, and there is no local equivalent to substitute.

The folder is kept so the sample is accounted for: `samples/samples.json` records it,
`setup-databases` reports it as skipped rather than silently ignoring it, and the link above
takes you to the upstream material if you want to run it by hand elsewhere.

## In the catalog

Nothing - this sample contributes no objects.

## Licence

The upstream material is MIT-licensed - see [the upstream licence](https://github.com/microsoft/sql-server-samples/blob/master/license.txt).
Nothing from it is vendored into this repository; `setup-databases` fetches it at provisioning time.
