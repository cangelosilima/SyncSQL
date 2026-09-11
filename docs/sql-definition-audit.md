# SQL definition audit

Updated 2026-09-08. This audit covers the objects supported by the CLI's MSSQL and Oracle extractors.

## Changes

| Object | Metadata previously omitted | Export behavior |
| --- | --- | --- |
| MSSQL schemas | Owner already queried but discarded; schema permissions and extended properties never queried | `CREATE SCHEMA ... AUTHORIZATION ...`, executable permission and property sections, saved as `<database>/<schema>/<schema>.sql` |
| MSSQL types | No type extraction | Alias types with base size/precision/scale/nullability; table types with columns, keys, checks and indexes; CLR external assembly/class references; bound default/rule references |
| Oracle types | TYPE and TYPE BODY absent from the extraction map | `DBMS_METADATA.GET_DDL` through the existing owner and object-name filters; `Types` and `TypeBodies` folders |
| Procedures, views, functions, triggers | Creation-time ANSI_NULLS and QUOTED_IDENTIFIER settings; disabled triggers | SET statements with batch boundaries; disabled trigger state; explicit unavailable-definition comment for encrypted/inaccessible modules |
| Tables and columns | Computed expressions/persistence, named defaults, user-defined type qualification, time precision, collation, rowguid, sparse/column-set/FILESTREAM and identity replication flags | Shared column renderer preserves these definitions; names containing `]` are escaped |
| Primary/unique constraints | Primary-key clustering omitted; unique constraints excluded from both table and index scripts | Explicit primary-key clustering and separate unique-constraint definitions |
| Foreign keys/checks | Referential actions, replication flag, disabled and trust state | CHECK/NOCHECK, delete/update actions, NOT FOR REPLICATION and disabled-state statements |
| Rowstore indexes | Filter predicates and persistent index options omitted | INCLUDE/WHERE, fill factor, padding, duplicate-key behavior, lock options and disabled state |
| Object/schema/type security | Schema/type permissions, GRANT WITH GRANT OPTION, column REVOKE exceptions, grantor, explicit ownership | Executable Permissions and Ownership sections, independently queried from description metadata |
| Extended properties | Only description-like object/column properties retained | All supported schema/type/object/column properties receive executable definitions retaining SQL variant base type and precision |
| Linked servers | Global login mappings filtered out; local login names, self-mappings without remote names, location and server options omitted | Explicit global and local mappings, default self-mapping removal before replay, location, RPC/data-access/collation/timeouts/transaction-promotion settings |

## Layout and compatibility

Schema definitions sit beside their object-type folders. Followed objects use `original-server/LinkedServers/link-name/database/schema/type/object.sql`, while `original-server/LinkedServers/link-name.sql` defines the link itself. Followed servers continue to exclude their own LinkedServers definitions.

The JSON `-- Identity:` header records the logical server/database/schema/type/name. It preserves catalog IDs, metrics keys and lineage independently of the export path. Old paths remain readable; history recognizes legacy and standalone schema-first paths before nesting. Existing generated trees are not migrated by editing CLI source: use a fresh extraction staging directory for the new layout.

## Remaining limits

- These exports document database objects; they are not a full database backup or a dependency-ordered deployment package. Roles/users, CLR assembly binaries, XML schema collections, bound rule/default objects and target linked-server credentials may need separate provisioning before replay.
- MSSQL temporal/ledger/graph/encryption features, table storage/partition placement and compression are not fully reconstructed. Specialized XML, spatial, columnstore and hash indexes on regular tables retain the existing informational fallback. Replication remains an informational publication/article snapshot.
- Passwords cannot be recovered from linked-login metadata; explicit remote-password mappings keep a placeholder. Catalog visibility still depends on the extraction account's permissions.
- Oracle object DDL continues to come from DBMS_METADATA. Schema/user DDL may fall back to an informational comment when inaccessible; Oracle grants and ancillary user configuration are not expanded by this change.
- Discovery retains configured depth and de-duplication rules. Links already covered by a configured or previously discovered target are skipped, with a logged reason, rather than duplicating the target extraction.

## Validation

Regression tests parse all MSSQL catalog queries and representative generated DDL, and cover authorization/escaping, module settings, type variants, column definitions, index and linked-server options, schema paths, nested remote reference resolution, and stable identities. The SQL Server 2022 validation harness binds every query to real catalog views, extracts synthetic fixtures, recreates them in a second database, and compares the resulting metadata. It uses a temporary localhost-only Docker container and removes it afterward.

Run `./cli/tests/SyncSql.Extraction.MsSql.IntegrationTests/Run-SqlValidation.ps1` from PowerShell with Docker running and .NET 10 installed. The xUnit integration project is included in `cli/SyncSql.slnx` and is skipped during ordinary test runs; the runner explicitly enables it. It uses `mcr.microsoft.com/mssql/server:2022-latest` and localhost port 15439 (override with `-Port`). The completed audit run checked 22 catalog queries, recreated eight definitions, and passed 11 source-versus-restored metadata comparisons. See the [integration test README](../cli/tests/SyncSql.Extraction.MsSql.IntegrationTests/README.md) for execution details.

Reference syntax was checked against Microsoft documentation for [CREATE SCHEMA](https://learn.microsoft.com/en-us/sql/t-sql/statements/create-schema-transact-sql), [CREATE TYPE](https://learn.microsoft.com/en-us/sql/t-sql/statements/create-type-transact-sql), [sys.sql_modules](https://learn.microsoft.com/en-us/sql/relational-databases/system-catalog-views/sys-sql-modules-transact-sql), and [sys.servers](https://learn.microsoft.com/en-us/sql/relational-databases/system-catalog-views/sys-servers-transact-sql).
