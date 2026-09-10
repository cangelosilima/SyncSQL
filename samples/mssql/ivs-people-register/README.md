# IVS people register

Ideographic Variation Sequence collations and OPENJSON shredding over Japanese names.

- **Upstream**: [`samples/demos/ivs-people-register`](https://github.com/microsoft/sql-server-samples/tree/master/samples/demos/ivs-people-register) in [`sql-server-samples`](https://github.com/microsoft/sql-server-samples)
- **Engine**: Microsoft SQL Server
- **Databases**: `IvsPeopleRegister`
- **Tier**: `standard` - provisioned by default.

## What gets installed

Into `IvsPeopleRegister`, in this order:

1. `sql-scripts/1 setup.sql` - from [upstream](https://github.com/microsoft/sql-server-samples/blob/master/samples/demos/ivs-people-register/sql-scripts/1%20setup.sql)
1. `sql-scripts/2 vss.sql` - from [upstream](https://github.com/microsoft/sql-server-samples/blob/master/samples/demos/ivs-people-register/sql-scripts/2%20vss.sql)

## In the catalog

Extracted objects land under:

```
samples/output/SAMPLES-MSSQL/IvsPeopleRegister/<schema>/<type>/<object>.sql
```

## Licence

The upstream material is MIT-licensed - see [the upstream licence](https://github.com/microsoft/sql-server-samples/blob/master/license.txt).
Nothing from it is vendored into this repository; `setup-databases` fetches it at provisioning time.
