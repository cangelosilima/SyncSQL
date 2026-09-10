# SH - Sales History

The star-schema warehouse: fact and dimension tables, materialized views and partitioning - the richest lineage of the three supported Oracle schemas.

- **Upstream**: [`sales_history`](https://github.com/oracle-samples/db-sample-schemas/tree/main/sales_history) in [`db-sample-schemas`](https://github.com/oracle-samples/db-sample-schemas)
- **Engine**: Oracle Database
- **Schemas**: `SH`
- **Tier**: `standard` - provisioned by default.

## What gets installed

`setup-databases` runs the upstream driver script `sales_history/sh_install.sql` through
SQL\*Plus inside the Oracle container, connected to `FREEPDB1` as `SYSTEM`. The script prompts for
a schema password, a tablespace and whether to overwrite an existing schema; the runner answers
them from `samples/.env` (`ORACLE_SAMPLE_PASSWORD`), the pluggable database's default tablespace,
and `YES` respectively - which is what makes re-provisioning repeatable.

To remove it again, run `sales_history/sh_uninstall.sql` the same way, or use
`setup-databases --down` to discard the whole container.

## In the catalog

Extracted objects land under:

```
samples/output/SAMPLES-ORACLE/FREEPDB1/SH/<type>/<object>.sql
```

## Licence

The upstream material is MIT-licensed - see [the upstream licence](https://github.com/oracle-samples/db-sample-schemas/blob/main/LICENSE.txt).
Nothing from it is vendored into this repository; `setup-databases` fetches it at provisioning time.
