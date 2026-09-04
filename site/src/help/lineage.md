# Lineage explorer

The full dependency graph, laid out automatically. Two modes sit behind the
tabs at the top.

## Browse

The attribute filter bar and the DDL content search box (both behave exactly
as they do on Explorer) decide which objects enter the graph.

- **Click** a node to drill into that object's own neighborhood *in place*.
  A breadcrumb trail and a **Back** button walk you out again.
- **Double-click** a node to open its full detail page.
- **1 / 2 / 3 hops** sets how far the neighborhood reaches around the
  focused object.
- **Copy link** hands over the current view exactly: filter tokens,
  drill-down focus, hop radius and content search all live in the URL.
  That link is the useful thing to paste into an incident write-up.

Arriving from an object page's "Open in Lineage" link seeds a real filter
token for that object, so clearing the drill-down narrows back to it rather
than dumping you into the whole catalog.

## Access

Search by grantee - a user, role or group - to see every object they hold a
GRANT or DENY permission on, down to the column where the grant is scoped
that way. Matches appear both as a table and in the graph, so you can go
from "what can this principal touch" straight into how those objects relate.

**Exact match** turns off substring matching, which matters when one role
name is a prefix of another. **Export CSV** writes one row per permission.

## Reading the graph

Edges carrying a known column-level reference are highlighted and labeled
with up to three column names. Click one to open a panel listing every
column that edge carries.

A linked server or database link is drawn as its own explicit hop, so a
cross-server dependency is visible rather than hidden inside an edge.

**Export SVG** and **Export PNG** render the currently visible graph as a
standalone image, built from node positions rather than by rasterizing the
page - it renders correctly outside the site and matches the active theme.

## What lineage does not cover

References are read off a real parse tree per engine (T-SQL ScriptDom for
MSSQL, an ANTLR4 PL/SQL grammar for Oracle), not by text matching - so
identifiers inside strings and comments are not mistaken for references.
But dynamic SQL and anything assembled at runtime are still invisible, and
traversals stop one hop past a linked-server boundary. Treat the graph as a
very good map, not a certified lineage report.
