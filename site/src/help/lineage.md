# Lineage explorer

The full dependency graph, laid out automatically, with one shared filter bar.

## Browse

Combine Server, Database, Schema, Type, Name, Description, DDL content and Grantee
chips to decide which objects enter the graph. Every chip must match. Plain text
searches object details and SQL; use **DDL content contains** for SQL-only search.
Remove any chip independently to broaden the results.

- **Click** a node to drill into that object's own neighborhood _in place_.
  Opening a search result clears the object filters used to locate it, so its
  callers and targets become visible. Grantee and DDL content filters remain.
  A breadcrumb trail and a **Back** button walk you out again.
- **Double-click** a node to open its full detail page.
- **View options** sets **Dependencies** (objects the focus references) and
  **Dependents** (objects that reference the focus) independently, from 0 to 20
  hops. Use **+ Add hop** to expand one direction; 0 hides that side.
  When a hop ends at a linked server or database link, the graph includes one
  extra hop in that direction to show callers or remote targets recorded for
  that object flow. Sharing a connection does not make unrelated objects part
  of the flow. Focusing the connection itself shows all its uses. When a catalog
  lacks caller-to-target records, expansion stops at the connection. Object
  detail graphs use this same context on both sides.
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
drilled into an object, they narrow _that object's neighborhood_ instead - so
adding "Type is StoredProcedures" while looking at a table shows the
procedures around it, rather than asking for something that is both. The
focused object always stays on screen, and the line under the breadcrumb says
how much of its neighborhood is being hidden. Connecting objects outside the
filters are retained and labeled so matches farther away keep a path to the focus.
If filters hide every surrounding object, **Show full neighborhood** clears them
while keeping the current focus and hop settings. Filters added while navigating
remain active when you focus another nearby object.

**Clear focus** converts the navigation into a real name filter as it
releases it, so you land on that one object rather than the whole catalog.

An edge drawn **dashed and labelled `dynamic`** was recovered from SQL built
as a string at runtime - an `OPENQUERY` body, an `EXEC` of a literal - rather
than read off the parse tree. It is a real relationship, held with less
certainty than the solid ones.

## Access

Add a **Grantee** chip - a user, role or group - to see every object they hold a
GRANT or DENY permission on, down to the column where the grant is scoped
that way. Matches appear both as a table and in the graph, so you can go
from "what can this principal touch" straight into how those objects relate.

**Grantee is** matches exactly; **Grantee contains** matches part of a name, and
**Grantee is in** accepts multiple names. Combine these with object and SQL chips.
The permissions table and **Export CSV** use the same filtered selection, with
one CSV row per matching permission. Retained graph connectors do not imply access.
Existing access and DDL-search links are converted into equivalent chips.

## Reading the graph

The compact **Graph legend** stays at the bottom of the graph area. Its summary
keeps the relationship direction and dynamic-SQL reminder visible. Expand it for
the complete key and object-type colors; collapse it to make room for analysis.

Edges carrying a known column-level reference are highlighted and labeled
with up to three column names. Click one to open a panel listing every
column that edge carries.

A linked server or database link is drawn as its own explicit hop, so a
cross-server dependency is visible rather than hidden inside an edge.

**Export SVG** and **Export PNG** render the currently visible graph as a
standalone image, built from node positions rather than by rasterizing the
page - it renders correctly outside the site and matches the site's light theme.
PNG exports use higher resolution, the page's loaded font, full wrapped object
names, curved edges and the same dashed references and focus highlight.
Both formats always include the full graph legend beneath the diagram, including
the object types shown, even when the on-screen legend is collapsed.

## What lineage does not cover

References are read off a real parse tree per engine (T-SQL ScriptDom for
MSSQL, an ANTLR4 PL/SQL grammar for Oracle), not by text matching - so
identifiers inside strings and comments are not mistaken for references.
Dynamic SQL that cannot be recovered from the extracted definitions remains
invisible. Cross-server paths require recorded caller-to-target references;
sharing a linked server alone does not establish a path. Treat the graph as a
very good map, not a certified lineage report.
