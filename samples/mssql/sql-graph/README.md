# SQL Graph recommendation system

Graph node and edge tables (Nodes./Edges. schemas) added to WideWorldImporters, plus MATCH queries over them.

- **Upstream**: [`samples/demos/sql-graph`](https://github.com/microsoft/sql-server-samples/tree/master/samples/demos/sql-graph) in [`sql-server-samples`](https://github.com/microsoft/sql-server-samples)
- **Engine**: Microsoft SQL Server
- **Databases**: `WideWorldImporters`
- **Requires**: [`wide-world-importers`](../wide-world-importers/)
- **Tier**: `standard` - provisioned by default.

## What gets installed

Into `WideWorldImporters`, in this order:

1. [`install/00-prepare.sql`](install/00-prepare.sql) - a SyncSQL prelude, not upstream material. Read it; it says what it fixes up.
1. `recommendation-system/before-you-begin.sql` - from [upstream](https://github.com/microsoft/sql-server-samples/blob/master/samples/demos/sql-graph/recommendation-system/before-you-begin.sql)
1. `recommendation-system/demo1-create-and-populate-nodes-and-edges.sql` - from [upstream](https://github.com/microsoft/sql-server-samples/blob/master/samples/demos/sql-graph/recommendation-system/demo1-create-and-populate-nodes-and-edges.sql)
1. `recommendation-system/demo3-create-and-populate-nodes-and-edges.sql` - from [upstream](https://github.com/microsoft/sql-server-samples/blob/master/samples/demos/sql-graph/recommendation-system/demo3-create-and-populate-nodes-and-edges.sql)

### Not installed

| Upstream path | Why |
|---------------|-----|
| `recommendation-system/demo2-using-the-match-clause.sql` | Query-only demo script - creates no objects. |
| `recommendation-system/demo3-recommendation-system-for-sales.sql` | Query-only demo script - creates no objects. |

## In the catalog

Extracted objects land under:

```
samples/output/SAMPLES-MSSQL/WideWorldImporters/<schema>/<type>/<object>.sql
```

## Licence

The upstream material is MIT-licensed - see [the upstream licence](https://github.com/microsoft/sql-server-samples/blob/master/license.txt).
Nothing from it is vendored into this repository; `setup-databases` fetches it at provisioning time.
