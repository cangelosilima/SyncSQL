# Query Tuning Assistant

The SSMS Query Tuning Assistant walkthrough after a compatibility-level upgrade.

- **Upstream**: [`samples/demos/query-tuning-assistant`](https://github.com/microsoft/sql-server-samples/tree/master/samples/demos/query-tuning-assistant) in [`sql-server-samples`](https://github.com/microsoft/sql-server-samples)
- **Engine**: Microsoft SQL Server
- **Tier**: `unsupported` - never provisioned; see *Why it is not installed* below.

## Why it is not installed

Ships a single qta-demo.zip of SSMS workload artifacts, with no SQL script to install. The walkthrough is driven from the SSMS UI.

The folder is kept so the sample is accounted for: `samples/samples.json` records it,
`setup-databases` reports it as skipped rather than silently ignoring it, and the link above
takes you to the upstream material if you want to run it by hand elsewhere.

## In the catalog

Nothing - this sample contributes no objects.

## Licence

The upstream material is MIT-licensed - see [the upstream licence](https://github.com/microsoft/sql-server-samples/blob/master/license.txt).
Nothing from it is vendored into this repository; `setup-databases` fetches it at provisioning time.
