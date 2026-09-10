# CO - Customer Orders

The modern e-commerce schema, including JSON columns for semi-structured order data.

- **Upstream**: [`customer_orders`](https://github.com/oracle-samples/db-sample-schemas/tree/main/customer_orders) in [`db-sample-schemas`](https://github.com/oracle-samples/db-sample-schemas)
- **Engine**: Oracle Database
- **Schemas**: `CO`
- **Tier**: `standard` - provisioned by default.

## What gets installed

`setup-databases` runs the upstream driver script `customer_orders/co_install.sql` through
SQL\*Plus inside the Oracle container, connected to `FREEPDB1` as `SYSTEM`. The script prompts for
a schema password, a tablespace and whether to overwrite an existing schema; the runner answers
them from `samples/.env` (`ORACLE_SAMPLE_PASSWORD`), the pluggable database's default tablespace,
and `YES` respectively - which is what makes re-provisioning repeatable.

To remove it again, run `customer_orders/co_uninstall.sql` the same way, or use
`setup-databases --down` to discard the whole container.

## In the catalog

Extracted objects land under:

```
samples/output/SAMPLES-ORACLE/FREEPDB1/CO/<type>/<object>.sql
```

## Licence

The upstream material is MIT-licensed - see [the upstream licence](https://github.com/oracle-samples/db-sample-schemas/blob/main/LICENSE.txt).
Nothing from it is vendored into this repository; `setup-databases` fetches it at provisioning time.
