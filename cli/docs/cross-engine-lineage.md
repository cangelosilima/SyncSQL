# Cross-engine destinations and lineage

Catalog assembly connects independently extracted servers by their endpoint metadata and explicit aliases. A database-link password is not needed to join an Oracle reference to an object already extracted from SQL Server with its own credentials. Keep both extractions under the objects root supplied to `catalog build`; extra `ORACLE/` and `MSSQL/` folder levels work for files carrying SyncSQL's identity header.

An Oracle database link preserves its owner, `HOST` connect identifier and `USERNAME` from `DBA_DB_LINKS`, falling back to `ALL_DB_LINKS` when necessary. These facts survive even if `DBMS_METADATA.GET_DDL` is unavailable. SQL Server links preserve their data source, catalog and visible login mappings. Passwords and password hashes are not extracted.

## Resolve an Oracle Transparent Gateway

Supply copies of the configuration used by the **source Oracle database and gateway hosts**, not an unrelated client's Oracle Net configuration. Paths below are relative to `servers.json`. SyncSQL reads these files; it does not copy their full contents into the export.

Add to the Oracle server entry:

```json
{
  "oracleNetwork": {
    "tnsNamesFile": "network/tnsnames.ora",
    "gateways": [
      {
        "host": "tg-development.example.com",
        "sid": "orders",
        "initFile": "network/initorders.ora",
        "targetEngine": "mssql"
      }
    ]
  }
}
```

For example, the relevant files might contain:

```ini
# tnsnames.ora
GATEWAY_DEVELOPMENT =
  (DESCRIPTION=
    (ADDRESS=(PROTOCOL=TCP)(HOST=tg-development.example.com)(PORT=1521))
    (CONNECT_DATA=(SID=orders))
    (HS=OK))

# initorders.ora (a separate file)
HS_FDS_CONNECT_INFO=sql-development.example.com:1433//Orders
```

The link retains `GATEWAY_DEVELOPMENT` and the gateway host/SID, while its destination becomes `sql-development.example.com,1433`, database `Orders`, engine `mssql`. Gateway host is optional in configuration when the SID alone uniquely selects a gateway. Supply it when different gateway hosts reuse a SID.

For an ODBC gateway, set `odbcIniFile` on the gateway entry. `HS_FDS_CONNECT_INFO` is then interpreted as the section name in that INI file; `Server` (or `Servername`), `Port` and `Database` are read from that section. Export Windows DSNs to this format or use an explicit destination mapping. Credentials and other ODBC settings are not exported.

The reader handles single-endpoint Oracle Net descriptors, aliases and dedicated SQL Server gateway destinations (`host[:port]/instance/database`, including named instances and bracketed IPv6). LDAP resolution, `IFILE` includes, Windows registry DSNs and remote filesystem discovery are not performed. Supply flattened configuration or an explicit mapping for these cases. Unavailable files and ambiguous routes retain the original pointer with diagnostics.

## Explicit mappings and independently extracted identities

When gateway configuration is unavailable, add this to the **source** server entry:

```json
{
  "linkTargets": [
    {
      "name": "DL_ORDER",
      "owner": "APP",
      "targetEngine": "mssql",
      "dataSource": "sql-development.example.com,1433",
      "database": "Orders"
    }
  ]
}
```

An owner-specific mapping takes precedence over an ownerless mapping. Specified fields override discovered values; both observations remain in the evidence list. For SQL Server linked servers, omit `owner`. Optional `defaultSchema` supplies independently verified schema context. Never set it to the login name merely because the names look related.

Extract the destination SQL Server normally with its own credentials. Use its actual `host`/`port`/instance or declared `aliases` to match the link's data source. Add `Logins` and `Users` to that server's effective `objectTypes` alongside the existing table/view/routine types. `Logins` supplies visible default databases; `Users` supplies the login-to-database-user mapping and default schema. These are metadata snapshots, not complete principal recreation scripts. SQL Server metadata permissions determine which identities are visible.

When one fixed remote login is identified, its independently extracted login can supply an otherwise unknown default database, and its mapped database user can supply a default schema. The catalog exposes clickable pointers to those principals. Login names alone never identify a destination server. Multiple competing endpoints or object matches remain unresolved.

## Partial visibility

The graph retains caller → database link/linked server, even when the remote object is unavailable. `linkedServerReferences` carries destination information and a status (`resolved`, `external`, `ambiguous`, or `not-observed`); unresolved references keep `to: null`. Missing remote objects are not declared dropped merely because an extraction account could not see them. Once a matching destination export is present, rebuild the catalog to connect the remaining path.

Each link's Connection destination panel shows its endpoint, database/schema, gateway, remote identities, evidence and diagnostics. Catalog assembly does not open remote sessions or test authentication. Existing SQL Server follow-up extraction remains separate from this cross-engine catalog join: new Oracle gateway targets should be configured as normal destination server entries with their own credentials.
