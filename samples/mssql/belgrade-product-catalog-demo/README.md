# Belgrade product catalog

JSON columns, system-versioned temporal tables, dynamic data masking, row-level security and a memory-optimized (XTP) table - the densest object graph of any demo here.

- **Upstream**: [`samples/demos/belgrade-product-catalog-demo`](https://github.com/microsoft/sql-server-samples/tree/master/samples/demos/belgrade-product-catalog-demo) in [`sql-server-samples`](https://github.com/microsoft/sql-server-samples)
- **Engine**: Microsoft SQL Server
- **Databases**: `ProductCatalog`
- **Tier**: `standard` - provisioned by default.

## What gets installed

Into `ProductCatalog`, in this order:

1. [`install/00-prepare.sql`](install/00-prepare.sql) - a SyncSQL prelude, not upstream material. Read it; it says what it fixes up.
1. `sql-scripts/1 setup.sql` - from [upstream](https://github.com/microsoft/sql-server-samples/blob/master/samples/demos/belgrade-product-catalog-demo/sql-scripts/1%20setup.sql)
1. `sql-scripts/3 setup-temporal.sql` - from [upstream](https://github.com/microsoft/sql-server-samples/blob/master/samples/demos/belgrade-product-catalog-demo/sql-scripts/3%20setup-temporal.sql)
1. `sql-scripts/5 data-masking.sql` - from [upstream](https://github.com/microsoft/sql-server-samples/blob/master/samples/demos/belgrade-product-catalog-demo/sql-scripts/5%20data-masking.sql)
1. `sql-scripts/6 rls.sql` - from [upstream](https://github.com/microsoft/sql-server-samples/blob/master/samples/demos/belgrade-product-catalog-demo/sql-scripts/6%20rls.sql)
1. `sql-scripts/7. xtp.sql` - from [upstream](https://github.com/microsoft/sql-server-samples/blob/master/samples/demos/belgrade-product-catalog-demo/sql-scripts/7.%20xtp.sql)

### Not installed

| Upstream path | Why |
|---------------|-----|
| `sql-scripts/0 cleanup.sql` | Drops everything the other scripts create. |
| `sql-scripts/2 json.sql` | Query-only demo script - creates no objects. |
| `sql-scripts/4 temporal.sql` | Query-only demo script - creates no objects. |
| `sql-scripts/8. bcp.sql` | Needs the bcp utility and an existing WideWorldImporters export. |
| `sql-scripts/9. string-agg.sql` | Query-only demo script - creates no objects. |
| `sql-scripts/A1 fraud-detection.sql` | Query-only demo script - creates no objects. |
| `sql-scripts/A2 compare-products.sql` | Query-only demo script - creates no objects. |
| `sql-scripts/A3 setup-temporal-3-part-name.sql` | Alternative to '3 setup-temporal.sql' - the two define History.Product incompatibly, so only one can be installed. |

## In the catalog

Extracted objects land under:

```
samples/output/SAMPLES-MSSQL/ProductCatalog/<schema>/<type>/<object>.sql
```

## Licence

The upstream material is MIT-licensed - see [the upstream licence](https://github.com/microsoft/sql-server-samples/blob/master/license.txt).
Nothing from it is vendored into this repository; `setup-databases` fetches it at provisioning time.
