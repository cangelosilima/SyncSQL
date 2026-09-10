# Plan comparison

Two saved .sqlplan files compared side by side in SSMS, plus a Query Store bacpac and a workload .exe.

- **Upstream**: [`samples/demos/Plan-Comparison`](https://github.com/microsoft/sql-server-samples/tree/master/samples/demos/Plan-Comparison) in [`sql-server-samples`](https://github.com/microsoft/sql-server-samples)
- **Engine**: Microsoft SQL Server
- **Tier**: `unsupported` - never provisioned; see *Why it is not installed* below.

## Why it is not installed

Ships no SQL. The offline half is two .sqlplan files opened in SSMS; the Query Store half is a Windows .exe plus a .bacpac that needs SqlPackage and an Azure SQL Database target.

The folder is kept so the sample is accounted for: `samples/samples.json` records it,
`setup-databases` reports it as skipped rather than silently ignoring it, and the link above
takes you to the upstream material if you want to run it by hand elsewhere.

## In the catalog

Nothing - this sample contributes no objects.

## Licence

The upstream material is MIT-licensed - see [the upstream licence](https://github.com/microsoft/sql-server-samples/blob/master/license.txt).
Nothing from it is vendored into this repository; `setup-databases` fetches it at provisioning time.
