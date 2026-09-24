# Default extraction exclusions

The .NET CLI applies engine-specific exclusions by default. Configure them in
`servers.json`, alongside the existing filters:

```json
{
  "defaults": {
    "useDefaultExclusions": true
  },
  "servers": [
    {
      "name": "SQL_ADMIN",
      "type": "mssql",
      "host": "sql.example.com",
      "integratedSecurity": true,
      "useDefaultExclusions": false,
      "databases": { "include": ["^master$"] },
      "objectTypes": ["Tables", "Views", "StoredProcedures"]
    }
  ]
}
```

The server value overrides the defaults value. If both are omitted or null, the
policy is enabled. Discovered SQL Server follow-ups inherit the parent's setting.
No separate command-line switch is needed: `syncsql sync --config ...` reads it.

Managed exclusions are added **after** resolving each explicit filter. A server's
`schemas`, `databases` or `objectNames` block still replaces the corresponding
defaults block, but cannot remove the managed exclusions. Set
`useDefaultExclusions: false` to disable the additional policy. Explicit regex
exclusions still win over includes, even with the policy disabled. Remove any
old system-name exclusions from your own filters if you want those names allowed.

| Engine | Additional exclusions when enabled |
| --- | --- |
| SQL Server | Databases `master`, `model`, `msdb`, `tempdb`; schemas `sys`, `INFORMATION_SCHEMA`. Name matching is case-insensitive. |
| Oracle | Oracle-maintained owners (`ALL_USERS.ORACLE_MAINTAINED = 'Y'`) and objects (`ALL_OBJECTS.ORACLE_MAINTAINED = 'Y'`), secondary data-cartridge objects, recycle-bin names beginning `BIN$`, and the known system-owner names below. |

SQL Server still connects to `master` to discover databases, logins and linked
servers. Those server-level exports remain available. If you keep application
procedures in `master`, use a server override as above.

`model` (the template for new databases) and `msdb` (SQL Server Agent jobs and
related metadata) are standard SQL Server system databases, like `master` and
`tempdb`. Databases named `sql` or `dba` are not standard system databases and
are not excluded by this policy or the sample configuration. Keep exclusions
for environment-specific databases in your own configuration.

Oracle's maintained-owner check also applies to database links, including when
the CLI falls back from `DBA_DB_LINKS` to `ALL_DB_LINKS` due to permissions.
`PUBLIC` is not excluded wholesale: application public synonyms and links remain
eligible. There is no blanket `SYS_`, `TMP_`, synonym or package exclusion.

On older Oracle versions without `ORACLE_MAINTAINED`, the CLI logs a warning and
falls back to known owner names, recycle-bin names and secondary-object metadata.
Only the missing-column error triggers this fallback; access and connection
failures are not silently treated as older Oracle versions. Known owner exclusions
are case-insensitive:

```text
SYS SYSTEM OUTLN DBSNMP AUDSYS XDB CTXSYS MDSYS WMSYS
ORDSYS ORDDATA ORDPLUGINS OLAPSYS OJVMSYS EXFSYS SI_INFORMTN_SCHEMA
APPQOSSYS DBSFWUSER DVSYS DVF LBACSYS GSMADMIN_INTERNAL ANONYMOUS
APEX_<digits> FLOWS_<digits> FLOWS_FILES
```

For older versions this list is a fallback, not a complete inventory of every
installed component. An explicit application-schema include list is still the
most precise way to restrict extraction.

Disabling this policy restores the previous extraction scope. It does not turn
SyncSQL into a full system-database exporter: existing supported-type and native
query restrictions still apply, including SQL Server's `is_ms_shipped = 0` and
Oracle's `generated = 'N'` / `temporary = 'N'` conditions.

This flag affects extraction. It does not filter files that are already present
when `catalog build` reads an export directory. Re-extract into a clean output
directory to measure the reduced catalog scope without stale files. Fewer objects
should reduce work, but this does not establish a memory budget for a 260,000-object,
839 MB export; that workload still needs representative catalog measurements.

References: [Oracle ALL_OBJECTS](https://docs.oracle.com/en/database/oracle/oracle-database/19/refrn/ALL_OBJECTS.html),
[Oracle ALL_USERS](https://docs.oracle.com/en/database/oracle/oracle-database/19/refrn/ALL_USERS.html),
[SQL Server system databases](https://learn.microsoft.com/en-us/sql/relational-databases/databases/system-databases),
[SQL Server catalog objects](https://learn.microsoft.com/en-us/sql/relational-databases/system-catalog-views/sys-all-objects-transact-sql).
