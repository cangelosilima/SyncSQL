# Azure SQL Edge demos

IoT and ONNX scoring demos for Azure SQL Edge.

- **Upstream**: [`samples/demos/azure-sql-edge-demos`](https://github.com/microsoft/sql-server-samples/tree/master/samples/demos/azure-sql-edge-demos) in [`sql-server-samples`](https://github.com/microsoft/sql-server-samples)
- **Engine**: Microsoft SQL Server
- **Tier**: `unsupported` - never provisioned; see *Why it is not installed* below.

## Why it is not installed

The folder is a readme that redirects to the separate microsoft/sql-server-samples Azure SQL Edge repository; it contains no scripts of its own, and the demos need the Azure SQL Edge image rather than SQL Server.

The folder is kept so the sample is accounted for: `samples/samples.json` records it,
`setup-databases` reports it as skipped rather than silently ignoring it, and the link above
takes you to the upstream material if you want to run it by hand elsewhere.

## In the catalog

Nothing - this sample contributes no objects.

## Licence

The upstream material is MIT-licensed - see [the upstream licence](https://github.com/microsoft/sql-server-samples/blob/master/license.txt).
Nothing from it is vendored into this repository; `setup-databases` fetches it at provisioning time.
