# Selected workflows — interaction contract draft

> Current-state notice (2026-09-07): This document retains the earlier design/implementation record. Later owner-approved changes supersede its sidebar, Object inspector/Definition/Properties, graph, navigation and Alerts descriptions. Use [Current implemented UX](current-experience.md) and [ADR-0001](../adr/0001-sqlsync-investigation-workspaces-and-alerts.md) for the current contract. Original Figma frames and validation results remain historical evidence, not updated screenshots.

Status: Gate 4 approved in D-012, including recorded prototype and validation limits. D-011 selects E1 / Object C / L3. This is the approved design contract, not implemented behavior. Keep the 114-entry [functional inventory](functional-parity-inventory.md) authoritative for current semantics. No new catalog fields, backend changes or dependencies are required.

## Shared investigation context

Keep five global destinations, existing routes and the catalog sidebar. Proposed tree behavior: branch activation expands/collapses; leaf activation opens its existing Object URL. A branch does not filter Explorer or Lineage. Long identities wrap or expose full text; do not shorten the accessible name. Render expanded branches on demand and bound large sibling lists.

Proposed desktop order: catalog context, workspace, optional inspector. Collapse catalog context before reducing the main work area. At constrained widths, inspector opens as an explicitly invoked drawer rather than covering work automatically. At narrow widths, preserve horizontal scrolling for tables and SQL; never remove data columns. Exact breakpoints await Gate 5 measurement at 1440, 1280, 1024, 390 and 200% zoom.

Persistent header includes qualified identity, description, current-snapshot meaning, warnings, exports and navigation to full Lineage. Inspector closure must never hide the only access to a warning or capability. Default light and retained dark mode follow D-010.

## Explorer — token bar

Purpose: find and open objects with dense, complete result information. Hierarchy: title/count, committed attribute tokens, token builder, separately labeled DDL search, export and sortable results.

| Trigger/state | Visible response and focus | State/data contract |
|---|---|---|
| Build a token | Attribute/operator/value controls with explicit labels and suggestions | Draft does not filter until committed; preserve every operator and value format |
| Commit/remove token | Update chips, count and results; removal has an accessible name identifying the token | Existing AND semantics and `filters` encoding; preserve unrelated parameters |
| Suggestion keyboard interaction | Arrow keys move through suggestions, Enter selects, Escape dismisses; ordinary Tab reaches adjacent controls | No new keyboard shortcut or global command palette |
| DDL input changes | Preserve typed value while debounced results settle; announce updated count politely | Existing 150ms debounce, appended definition sections and `q` behavior |
| No matches | Keep all active criteria visible, show zero matches and disabled CSV | Do not auto-clear filters or collapse scope to a different database |
| More than 500 matches | Show explicit visible/total count; retain horizontal scroll and eight fields | First 500 sorted rows; CSV includes ALL matching sorted objects and existing export fields |
| Sort | Visible direction plus `aria-sort`; do not move keyboard focus | Existing seven sortable fields, default name ascending and toggle rules |
| Open object | Existing object route | Restoring originating sort/scroll via a new return action remains PEP-01, not silently included |
| Reload malformed URL | Use current decoder fallback and limits | No new serialization or broader acceptance of malformed tokens |

Fixture: SQLPROD01 + AppDb yields six matches. The comparison's three rows are an excerpt. No display limit changes are proposed.

## Object — IDE inspector

Purpose: inspect SQL and relationship evidence together while retaining full technical sections. Main workspaces: Definition, Columns, Graph/Relationships, Access, Metrics, History and Diff. A compact workspace switcher is local to the Object workbench; it does not replace global navigation. Inspector modes: Relationships and Properties. Do not invent editing, SQL execution, draggable docking or a terminal.

Proposed initial workspace: Definition, except an incoming `column` parameter opens Columns and exposes that column's existing consumer panel. Workspace selection remains local; do not introduce a `view` query parameter. Keep selected historical revision and comparison picks while switching workspaces within the same object; reset them on object identity change as required by the existing contract. Exact browser-back behavior must be exercised before implementation approval.

| Trigger/state | Main workspace | Inspector/header behavior |
|---|---|---|
| Open normal object | Current Definition and appended sections | Current metadata; Depends on / Used by remain distinct |
| Open `?column=Total` | Columns with Total consumer evidence visible | Preserve full identity and best-effort lineage explanation |
| Select another workspace | Full section with its original tables, charts, help and exports | Inspector stays optional; never replaces full section data |
| Close inspector | Reclaim width without losing selected workspace | Reopen button stays visible; keyboard focus returns to it |
| Follow related object | Existing target Object URL | New identity resets revision/comparison state; no stale prior-object evidence |
| Pick available historical revision | Definition displays selected historical DDL | Banner states that properties, grants and metrics remain current snapshot data |
| Historical revision has no DDL | History keeps that revision disabled with its existing explanation | Do not make unavailable revisions selectable; preserve defensive missing-content diff handling |
| Enable compare/select revisions | Diff destination uses existing ordered picks and replacement semantics | SHA/time context remains visible; no invented historical metadata |
| Identical or oversized diff | Existing identical result or >2000-line guard, with revision controls accessible | Do not display missing DDL as an empty successful diff |
| No columns/history/grants/metrics | Reachable workspace explains absence using current evidence | Zero is not substituted for unavailable extraction |
| Orphan/system/remote/dynamic references | Full Relationship workspace retains each existing kind and explanation | Orphan warning remains reachable regardless of inspector mode |
| Object not found | Existing not-found meaning with normal shell navigation | No stale metadata or fabricated replacement object |

Header CSV/XLSX exports retain current-object contents independent of active workspace or historical DDL. Section exports retain their complete existing rows. Metrics use MetricsPanels; grants preserve scope, GRANT/DENY and grantee navigation; relationship grouping/search, tags, expansion thresholds and embedded graph limits remain unchanged.

## Lineage — graph + inspector

Purpose: follow relationships without leaving the current investigation to identify focus or inspect edge evidence. Keep Browse/Access controls above the workspace, explicit global versus neighborhood filtering, focus breadcrumbs, hop controls and graph exports. Inspector follows existing focus; it does not create a second selection-only click mode.

| Trigger/state | Graph and navigation | Inspector |
|---|---|---|
| No focus, Browse | Whole-catalog selection with existing filtering and cap behavior | Explain that focus evidence appears after drill-down; do not choose an arbitrary object |
| Single-click real node | Existing drill-down and focus-stack behavior | Update to the new current focus without a separate layout traversal |
| Double-click real node | Existing Object navigation | Do not intercept it as inspector activation |
| Click edge | Preserve recorded-column inspection and existing graph semantics | Show edge evidence with source/target identities; no inferred missing columns |
| Change filter while focused | Filter neighborhood, retain root, preserve existing hop semantics | Keep current root even when its type fails the filter |
| Breadcrumb/back | Existing truncation and root-disabled behavior | Follow restored focus; clear stale edge evidence |
| Clear focus | Preserve existing name-token transition to Browse | Clear focus-specific evidence |
| Close/reopen inspector | Keep URL, focus, hops, filters and graph semantics unchanged | Proposed local panel visibility; no cross-session persistence |
| More than 300 selected objects | Existing selection breakdown and Browse narrowing | Never suggest inspector bypasses the graph cap |
| Bundled hub | Existing member expansion, close and graph controls | Bundles are groups, not real catalog objects |
| Switch to Access | Preserve current mode-switch/reset and URL behavior | Keep grantee results and grants primary; no inferred effective access |
| Exact/substring grantee search | Preserve suggestions, counts, object grants and full permission CSV | Exact remains local, not newly URL-persisted |

Fixture: at dbo.Orders, one hop includes four objects; a StoredProcedures filter shows two including the focused table. Edge evidence includes Id and Total. This small fixture validates scope explanation, not large-graph performance.

PEP-10's inspector presentation is selected by D-011. Detailed dismissal, focus handling and narrow-width behavior above remain for Gate 4 review. PEP-09's separate Browse list is not selected. PEP-01 return context, PEP-07 tree mechanics and all enrichment remain separately bounded.

## Component mapping and parity validation

| Design element | Engineering mapping | Required evidence |
|---|---|---|
| Token builder / DDL search | Refactor existing FilterBar and ContentSearchBar | Operators, keyboard, committed versus draft state, URL reload and debounce |
| Dense results | Refactor Explorer composition; reuse CSV helpers | Eight fields, all sorts, 500/501 and full export |
| Object workspaces | Refactor ObjectPage composition; new reusable workspace switcher and InspectorPanel | Column deep links, state lifetime, missing sections, historical/current separation |
| Definition / Diff / Metrics | Reuse CodeBlock, DiffView and MetricsPanels | No lost appended sections, revision guards or metric precision |
| Relationships | Reuse RelatedObjects and existing column-consumer logic | Grouping, searches, expansion caps and explanations |
| Graph and toolbar | Refactor composition around existing LineageGraph | Single/double clicks, focus, hops, cap/bundles, edges and SVG/PNG |
| Inspector drawer | New reusable responsive panel primitive | Label, close/reopen, Escape, focus return; modal trap only when modal |
| Exports / help / badges | Reuse existing CsvExportButton, XlsxExportButton, HelpButton and TypeBadge | Full contents, filenames, scope and accessible names |

## Connected review and validation limits

The selected Figma frames now include explicit blue review links. They connect eight annotated state sheets and return paths. This is a navigable review aid, not a simulation of token input, sorting, downloads, revision selection or graph gestures.

| State sheet | Evidence represented |
|---|---|
| [Explorer no matches](https://www.figma.com/design/yAIbDoFO7bJd7Vm22nCc6p?node-id=62-2) | Remove unmatched name token; return to existing six-match fixture |
| [Explorer 501 matches](https://www.figma.com/design/yAIbDoFO7bJd7Vm22nCc6p?node-id=62-10) | Synthetic scale specification, not fabricated catalog totals |
| [Object Columns / Total](https://www.figma.com/design/yAIbDoFO7bJd7Vm22nCc6p?node-id=62-18) | Existing column and known-consumer evidence |
| [Historical DDL unavailable](https://www.figma.com/design/yAIbDoFO7bJd7Vm22nCc6p?node-id=62-26) | Synthetic disabled revision state; no invented SHA |
| [Oversized diff](https://www.figma.com/design/yAIbDoFO7bJd7Vm22nCc6p?node-id=62-34) | Existing guard placement; synthetic comparison |
| [Lineage inspector closed](https://www.figma.com/design/yAIbDoFO7bJd7Vm22nCc6p?node-id=62-42) | Same focus/filter/hops; reopen returns to selected layout |
| [Access](https://www.figma.com/design/yAIbDoFO7bJd7Vm22nCc6p?node-id=62-50) | Existing grant excerpt; no invented matching totals |
| [Graph cap](https://www.figma.com/design/yAIbDoFO7bJd7Vm22nCc6p?node-id=62-58) | Synthetic 301-selection state; distinct from bundled focus |

Figma reaction readback verified 19 explicit navigation links and valid target IDs. Canvas bounds do not overlap. Selected Object, Columns, closed inspector, no-match, missing-DDL and graph-cap sheets were visually inspected. These sheets use annotations in place of fully rendered controls. Remaining states in the tables are specified only. Keyboard behavior, responsive layouts, downloads, graph gestures and full interactive fidelity remain validation work for high-fidelity design and implementation; this is not a claim of functional testing.

Production implementation remains behind Gate 5. No application changes or new tests were needed to record this model choice; the prior 214-test baseline remains the latest runtime evidence.
