# ADR 0002: Schema-first export paths with stable catalog identities

Status: Accepted, 2026-09-07 — requested by the product owner.

## Context

The catalog navigation and extracted SQL tree should follow Server → Database → Schema → Type → Object. Previously, SQL files used Type before Schema, and their relative paths also defined object identities used by deep links, metrics and revision history.

## Decision

New SQL files use `server/database/[schema/]type/object.sql`. Objects without a schema keep Type under the database, including `_ServerLevel` for server-scoped objects. The catalog sidebar follows the same hierarchy.

New exports carry `-- Path layout: schema/type` in their header. The catalog builder uses this marker rather than guessing whether a directory name denotes a schema or type, and continues to read unmarked legacy files. Object IDs retain their previous format; metrics snapshot/history keys and routes remain unchanged. The catalog's `path` field records the actual SQL file path.

Git mining maps current file paths to stable IDs, accepts legacy paths, counts a layout move once per object per commit, and fetches historical SQL using the path recorded for that revision.

## Consequences

The next extraction relocates schema-scoped SQL files. Update external scripts that rely on the old folder layout, and use the updated catalog builder with new exports. Existing deep links and metrics joins remain valid. The layout migration itself may appear as a change in Git history; it is not a database DDL change. Existing extracted trees remain readable without manual migration.

## Validation

Tests cover schema-first export paths, stable IDs, legacy/new catalog loading (including a schema named `Views`), history before and after a file move, and type branches for schema-, database- and server-scoped objects. Site tests also cover the complete Overview Alerts count and investigation link.
