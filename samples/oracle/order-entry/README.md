# OE - Order Entry (archived)

The intermediate-complexity schema, with object types, XMLType and spatial columns.

- **Upstream**: [`order_entry`](https://github.com/oracle-samples/db-sample-schemas/tree/main/order_entry) in [`db-sample-schemas`](https://github.com/oracle-samples/db-sample-schemas)
- **Engine**: Oracle Database
- **Schemas**: `OE`
- **Tier**: `unsupported` - never provisioned; see *Why it is not installed* below.

## Why it is not installed

Archived upstream and no longer maintained. Unlike HR/CO/SH there is no <schema>_install.sql driver: installing it means running a dozen scripts in order, importing PurchaseOrders.dmp with Data Pump, loading .dat files with SQL*Loader and registering XDB resources - none of which the upstream repository automates or still tests.

The folder is kept so the sample is accounted for: `samples/samples.json` records it,
`setup-databases` reports it as skipped rather than silently ignoring it, and the link above
takes you to the upstream material if you want to run it by hand elsewhere.

## In the catalog

Extracted objects land under:

```
samples/output/SAMPLES-ORACLE/FREEPDB1/OE/<type>/<object>.sql
```

## Licence

The upstream material is MIT-licensed - see [the upstream licence](https://github.com/oracle-samples/db-sample-schemas/blob/main/LICENSE.txt).
Nothing from it is vendored into this repository; `setup-databases` fetches it at provisioning time.
