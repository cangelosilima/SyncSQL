# Live Query Statistics

Two long-running AdventureWorks queries meant to be watched live in SSMS. They create no objects, so the demo contributes its base database only.

- **Upstream**: [`samples/demos/LQS`](https://github.com/microsoft/sql-server-samples/tree/master/samples/demos/LQS) in [`sql-server-samples`](https://github.com/microsoft/sql-server-samples)
- **Engine**: Microsoft SQL Server
- **Databases**: `AdventureWorks2022`
- **Requires**: [`adventure-works`](../adventure-works/)
- **Tier**: `standard` - provisioned by default.

## Why it is not installed

Query-only: both scripts are SELECTs against AdventureWorks, written for SSMS's Live Query Statistics pane. Nothing to install - `adventure-works` already provides the objects they read.

The folder is kept so the sample is accounted for: `samples/samples.json` records it,
`setup-databases` reports it as skipped rather than silently ignoring it, and the link above
takes you to the upstream material if you want to run it by hand elsewhere.

## In the catalog

Extracted objects land under:

```
samples/output/SAMPLES-MSSQL/AdventureWorks2022/<schema>/<type>/<object>.sql
```

## Licence

The upstream material is MIT-licensed - see [the upstream licence](https://github.com/microsoft/sql-server-samples/blob/master/license.txt).
Nothing from it is vendored into this repository; `setup-databases` fetches it at provisioning time.
