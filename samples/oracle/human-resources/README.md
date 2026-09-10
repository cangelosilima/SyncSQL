# HR - Human Resources

The small introductory schema: 7 tables, a view, 2 procedures and a trigger. The best first thing to look at in the catalog.

- **Upstream**: [`human_resources`](https://github.com/oracle-samples/db-sample-schemas/tree/main/human_resources) in [`db-sample-schemas`](https://github.com/oracle-samples/db-sample-schemas)
- **Engine**: Oracle Database
- **Schemas**: `HR`
- **Tier**: `standard` - provisioned by default.

## What gets installed

`setup-databases` runs the upstream driver script `human_resources/hr_install.sql` through
SQL\*Plus inside the Oracle container, connected to `FREEPDB1` as `SYSTEM`. The script prompts for
a schema password, a tablespace and whether to overwrite an existing schema; the runner answers
them from `samples/.env` (`ORACLE_SAMPLE_PASSWORD`), the pluggable database's default tablespace,
and `YES` respectively - which is what makes re-provisioning repeatable.

To remove it again, run `human_resources/hr_uninstall.sql` the same way, or use
`setup-databases --down` to discard the whole container.

## In the catalog

Extracted objects land under:

```
samples/output/SAMPLES-ORACLE/FREEPDB1/HR/<type>/<object>.sql
```

## Licence

The upstream material is MIT-licensed - see [the upstream licence](https://github.com/oracle-samples/db-sample-schemas/blob/main/LICENSE.txt).
Nothing from it is vendored into this repository; `setup-databases` fetches it at provisioning time.
