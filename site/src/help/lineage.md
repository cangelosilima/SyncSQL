# Lineage explorer

The full dependency graph, laid out automatically. Two modes sit behind the
tabs at the top.

## Browse

The attribute filter bar and the DDL content search box decide which objects
enter the graph. The filter bar supports the same chips as Explorer; the
separate DDL box also lets you narrow the graph as you type.

- **Click** a node to drill into that object's own neighborhood *in place*.
  A breadcrumb trail and a **Back** button walk you out again.
- **Double-click** a node to open its full detail page.
- **View options** sets **Dependencies** (objects the focus references) and
  **Dependents** (objects that reference the focus) independently, from 0 to 6
  hops. Use **+ Add hop** to expand one direction; 0 hides that side.
- **Group intermediate layers** combines intermediate objects of the same type,
  direction and hop into counted nodes. The focus and outer objects stay visible.
  Click a group to see its members and focus an individual object. Group edges
  count actual references; they do not imply every member references every object.
- The focused object briefly glows when focus changes. Reduced-motion settings
  keep the static highlight without animation.
- **Copy link** hands over the current view exactly: filter tokens,
  drill-down focus, directional hop counts, grouping and content search all live in the URL.
  That link is the useful thing to paste into an incident write-up.

**Filters mean different things depending on whether you are navigating.**
With nothing focused, they select from the whole catalog. Once you have
drilled into an object, they narrow *that object's neighborhood* instead - so
adding "Type is StoredProcedures" while looking at a table shows the
procedures around it, rather than asking for something that is both. The
focused object always stays on screen, and the line under the breadcrumb says
how much of its neighborhood is being hidden. Connecting objects outside the
filters are retained and labeled so matches farther away keep a path to the focus.

**Clear focus** converts the navigation into a real name filter as it
releases it, so you land on that one object rather than the whole catalog.

An edge drawn **dashed and labelled `dynamic`** was recovered from SQL built
as a string at runtime - an `OPENQUERY` body, an `EXEC` of a literal - rather
than read off the parse tree. It is a real relationship, held with less
certainty than the solid ones.

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
