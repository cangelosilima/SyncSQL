# SQLineage — current implemented UX

Brand update (2026-09-07): the app is now SQLineage. A teal database-and-branching-arrows SVG is shared by the header and favicon. Page title and workbook creator metadata use the new name. Repository/CLI/package identifiers and deployment routes remain unchanged. Historical SQLSync/SyncSQL design records retain their original names.

Updated: 2026-09-11. This document is the current UX contract for the local implementation. Later owner instructions supersede the earlier approved Figma compositions where explicitly recorded here. The Figma file has not been updated in this documentation pass. Nothing here claims a deployment or a commit.

Changes since 2026-09-07, all recorded in their sections below: the dark theme and its toggle were removed (the app is light-only); Explorer and Lineage share one filter bar in which DDL content and grantee are ordinary attributes, so Lineage no longer has Browse/Access tabs; hop selection became two independent directional counts with optional grouping of intermediate layers; the graph legend moved below the graph and is now included in exported images; the object definition gained an Original/Formatted view toggle; and every route names itself in the document title. Where an earlier record in this folder still describes a dark theme, a separate DDL search box, a `tab=access` mode or a legend above the graph, this document supersedes it.

## Navigation and shell

Visual refinement (owner request, 2026-09-07): ivory light surfaces, warm charcoal text, muted teal accents and sage selection. Shared panel/control radius is 6px. Technical density, type/status colors and behavior remain unchanged. SQL keeps its dark code background, now warm charcoal - a code surface, not a theme.

Theme (2026-09-11): the application is light-only. The theme toggle, the stored preference and `ThemeContext` were removed; `tokens.css` keeps one palette, and the graph, its exported images and the code surface all assume it. Earlier records describing a retained dark mode are historical.

Primary order: **Overview → Explorer → Lineage → Alerts → AI → History**. AI retains its existing availability/disabled states. Snapshot status remains; the redundant server badges were removed. An example catalog additionally shows an **Example catalog** badge beside the brand, and the Overview opens with the interactive example panel (workload-user picker and benchmark lineage paths).

Every view names itself in the document title: `Overview | SQLineage` for plain pages, `schema.object · server/database | SQLineage` on an object (with the open column appended and `_ServerLevel` omitted), and the same identity prefixed with `Lineage · ` for a focused graph. Titles are applied before paint, including on Back/Forward and after the catalog loads; no history entry is added or replaced.

All routes remain inside HashRouter:

| Route | Purpose | URL state |
|---|---|---|
| `/` | Catalog intelligence and snapshot overview | None added |
| `/explorer` | Dense filtered object results, complete matching CSV | `filters` (chips, DDL content among the attributes); a legacy `q` opens as a DDL content chip |
| `/object/*` | Definition and object workspaces; slash-containing IDs retained | Existing `column`; other workspace and revision selections remain local |
| `/lineage` | One filtered investigation surface with persistent inspector | `filter` (chips, including grantee and DDL content), `focus`, `dependencies`/`dependents` (or `hops` when both match), `group`; legacy `q`, `tab=access`, `grantee`/`exact` open as equivalent chips |
| `/alerts` | All detected metric anomalies and orphaned references | New `category` (`all`, `metrics`, `orphans`) and `q`; default category omitted |
| `/ai` | Local interpretation and preview | Ordinary filters transfer to Explorer; resolved column requests link to Object `column` |
| `/history` | Mined commit chronology | No new state |

Catalog sidebar appears **only on Explorer and Object** and is always rendered there, with no Catalog toggle or Close button. Branches still expand/collapse, retain per-branch limits and Show more, and navigate without applying filters. The hierarchy is Server → Database → Schema → Type → Object. Objects without a schema retain Type directly under the database (`_ServerLevel` for server-scoped objects), without an artificial schema branch. At widths up to 1280px the catalog stacks above content with a bounded scroll area.

## Object explorer

The heading is **Object explorer**, with Help adjacent. The object name stays in the breadcrumb; server/database/schema context, description and type badge remain in the page metadata.

**Definition is always visible above the tabs**, with an **Original / Formatted** view toggle. Formatted re-indents the captured SQL in the browser through `config/sql-style.json`'s `format` section (dialect detected per definition, column alignment optional); it is a display transform only, so exports, diffs and the catalog keep the captured text, and an unparseable definition falls back to the original with a note. Historical definition selection and Back to latest remain supported. Additional definition sections remain expandable and are shown for the current definition. Lower tabs, in order:

1. Columns (initial workspace, also selected by `column` deep links)
2. Graph
3. Access
4. Relationships
5. Metrics
6. History
7. Diff

Relationships contains Depends on and Used by, with **All relationship evidence** at the top right opening Graph. Graph retains the detailed evidence sections. There is no Object inspector. The duplicated Properties section was first moved below the workspaces, then explicitly removed by the owner; it must not be restored from an older design document.

Revision selection updates the always-visible definition while retaining History as the lower workspace. Comparison picks survive workspace changes. Current metadata/grants/metrics are not presented as historical data. Existing exports, permissions, special references, columns, SQL sections and warnings retain their source semantics.

Optimizer statistics spans the full metrics grid, with a bounded, keyboard-focusable horizontal table scroll region. Other metric panels wrap responsively. Column lineage has all four panel borders; an empty consumer list retains its best-effort evidence explanation.

## Lineage and relationship display

Only Lineage has an Inspector. It is always rendered, with no toggle or Close button; it stacks beneath the graph at widths up to 1024px. Focused-object and selected-edge evidence remain available. Catalog filtering versus focused-neighborhood filtering, caps, bundles and navigation history remain unchanged.

Filtering and navigation (2026-09-11): Browse and Access are no longer modes. One filter bar carries the object attributes plus **DDL content** and **Grantee**, every chip must match, and a grantee chip additionally renders the permissions table and its CSV above the graph. Opening a search result clears object chips while retaining grantee and DDL chips. Objects outside the filters that keep a match connected to the focus are retained and labeled as connecting objects, with a count line; **Show full neighborhood** clears the filters while keeping focus and hops. Hop selection is two independent counts - **Dependencies** and **Dependents**, 0 to 20 each, with **+ Add hop** - in a **View options** panel beside the graph, together with **Group intermediate layers**, which collapses same-type/hop intermediates into counted dashed nodes while keeping the focus and outer objects visible. A hop ending on a linked server or database link extends one further hop in that direction where the catalog records caller-to-target references. Focus changes briefly highlight the focused node, static under reduced-motion settings.

The existing **React Flow + Dagre** stack is retained. No ELK or Cytoscape dependency was added. Layout now aligns source/target handles with its direction (right/left for the horizontal layout), uses the same width and estimated label-aware height for positioning and rendering, and increases spacing. It is not a measured-text layout or a guarantee of crossing-free routing on large graphs.

- Arrowheads point from the referencing object to the referenced object, including bundled edges.
- Related-object lists use outgoing/incoming arrows with accessible direction labels.
- Column-labeled edges retain recorded evidence; dynamic edges remain dashed and weaker evidence.
- Current focus uses the selected background; node borders retain type colors.
- Minimap removed by request. Zoom, fit, selection, drill-down, full evidence and exports remain.
- Full Lineage uses one outer graph border, avoiding a duplicate inner frame and top gap.
- A compact **Graph legend** is docked at the bottom of the graph area through native `details`/`summary`, collapsed to a direction and dynamic-SQL reminder and expandable to the full key plus the object types in the selected graph. Exported images always carry the full legend, object types included, regardless of the on-screen state.
- SVG/PNG generation uses rendered/measured dimensions when available, falls back to styled height, and connects horizontal graph edges at node boundaries. PNG renders at higher resolution with the page's loaded font, wrapped object names and curved edges; SVG stays a simplified representation rather than a capture of React Flow's exact rendering.

## Alerts — explicitly requested feature

Alerts consolidates the existing detector's complete findings and catalog orphaned references. It does not add collection, thresholds, severity scoring, live polling, acknowledgements, incident status or persistence of resolutions.

Category and case-insensitive text search filter by object identity/context and evidence. Counts include the complete matching set. Initially 100 rows are visible; Show more reveals another 100. Each available source object has Inspect object and Explore lineage links. Inspect object opens its normal page; it does not preselect Metrics. Missing source objects retain their ID and explain that investigation navigation is unavailable.

Metric anomalies are labeled **Heuristic**; orphaned references are **Catalog finding**. Detection explanation and Alerts Help document the existing thresholds, scope and data limitations. Missing orphan analysis is distinguished from an empty finding set. Filters survive through shareable URLs. There is no Alerts export or background notification feature.

Overview has five cards: Total objects, Commits mined, Lineage edges, Alerts, Last change. Alerts shows the complete anomaly + orphan total and category breakdown, linking to Alerts. The two detailed preview panels have been removed; evidence and explanations remain in Alerts. Cards use 16px gaps and wrap on small screens.

SQL exports now use `server/database/[schema/]type/object.sql`. New files identify the layout in their header; the catalog also accepts legacy files. IDs, routes and metrics keys stay stable. Git history reads SQL from the path at each revision. See [ADR 0002](../adr/0002-schema-first-export-paths.md).

## AI correction

The explicit request “Show all references to column id from Orders” previously became a misleading high-confidence literal DDL query. The planner now resolves an exact catalog object and column, counts recorded consumers with the existing column-lineage helper, and links to the existing Object column workspace. Qualified names disambiguate objects. Unknown/ambiguous targets and unsupported compound constraints block application with an explanation. Explicit quoted DDL searches retain literal meaning. The optional column target is an in-memory plan addition; existing catalog schema and local-model privacy are unchanged. This is bounded phrase support, not arbitrary natural-language lineage inference.

## Fixture and small corrections

The demo linked server SQL_A was renamed REMOTE2 in `site/public/data/catalog.json`, including its ID, DDL and references. This was a fixture edit, not SQL executed against a server. The actual remote hostname/database and function identifiers were not renamed. Related-name/type/dynamic badges have explicit spacing. Search containers supply one focus outline rather than duplicate inner and outer outlines.

## Validation and remaining work

See [validation](validation.md), [test output](latest-tests.log), and [ADR-0001](../adr/0001-sqlsync-investigation-workspaces-and-alerts.md). Earlier screenshots and test counts belong to the implementation stage when recorded. Browser spot checks verified graph direction/frame, exact AI column request, metrics containment, persistent definition, sidebar scope, and Alerts presentation. They do not certify every screen at every viewport.

Full accessibility, screen-reader, cross-browser, 200% zoom, large-estate performance and completed native download certification remain open. Figma synchronization and any trial of ELK are not completed. Unapproved enrichment proposals remain proposals except the explicitly scoped Alerts capability described above.
