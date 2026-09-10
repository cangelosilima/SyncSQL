# PM - Product Media (archived)

Print-media LOB types: images, audio, video and formatted documents.

- **Upstream**: [`product_media`](https://github.com/oracle-samples/db-sample-schemas/tree/main/product_media) in [`db-sample-schemas`](https://github.com/oracle-samples/db-sample-schemas)
- **Engine**: Oracle Database
- **Schemas**: `PM`
- **Tier**: `unsupported` - never provisioned; see *Why it is not installed* below.

## Why it is not installed

Archived upstream. Its data is binary media loaded with SQL*Loader through LOB control files, and its column types depend on Oracle Multimedia, which was desupported in 19c and is absent from Oracle Database Free 23ai.

The folder is kept so the sample is accounted for: `samples/samples.json` records it,
`setup-databases` reports it as skipped rather than silently ignoring it, and the link above
takes you to the upstream material if you want to run it by hand elsewhere.

## In the catalog

Extracted objects land under:

```
samples/output/SAMPLES-ORACLE/FREEPDB1/PM/<type>/<object>.sql
```

## Licence

The upstream material is MIT-licensed - see [the upstream licence](https://github.com/oracle-samples/db-sample-schemas/blob/main/LICENSE.txt).
Nothing from it is vendored into this repository; `setup-databases` fetches it at provisioning time.
