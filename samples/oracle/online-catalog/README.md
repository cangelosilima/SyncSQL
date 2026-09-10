# OC - Online Catalog (archived)

Object-relational views and types built on top of the OE schema.

- **Upstream**: [`order_entry`](https://github.com/oracle-samples/db-sample-schemas/tree/main/order_entry) in [`db-sample-schemas`](https://github.com/oracle-samples/db-sample-schemas)
- **Engine**: Oracle Database
- **Schemas**: `OC`
- **Tier**: `unsupported` - never provisioned; see *Why it is not installed* below.

## Why it is not installed

Archived upstream, and built inside the OE schema (the oc_*.sql scripts live in order_entry/) - so it inherits every OE prerequisite above.

The folder is kept so the sample is accounted for: `samples/samples.json` records it,
`setup-databases` reports it as skipped rather than silently ignoring it, and the link above
takes you to the upstream material if you want to run it by hand elsewhere.

## In the catalog

Extracted objects land under:

```
samples/output/SAMPLES-ORACLE/FREEPDB1/OC/<type>/<object>.sql
```

## Licence

The upstream material is MIT-licensed - see [the upstream licence](https://github.com/oracle-samples/db-sample-schemas/blob/main/LICENSE.txt).
Nothing from it is vendored into this repository; `setup-databases` fetches it at provisioning time.
