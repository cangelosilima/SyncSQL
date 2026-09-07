# Gate 4 — Core workflow model comparison

> Current-state notice (2026-09-07): This document retains the earlier design/implementation record. Later owner-approved changes supersede its sidebar, Object inspector/Definition/Properties, graph, navigation and Alerts descriptions. Use [Current implemented UX](current-experience.md) and [ADR-0001](../adr/0001-sqlsync-investigation-workspaces-and-alerts.md) for the current contract. Original Figma frames and validation results remain historical evidence, not updated screenshots.

Status: owner selected **E1 Token bar / Object C IDE inspector / L3 Graph + inspector** (D-011). Detailed Gate 4 behavior approval remains open. Gates 1–3 are approved. Light Developer Tool styling, dense results and balanced Object detail follow D-010. Selected wireframes now have explicit navigation links to annotated state sheets; input controls and graph gestures are not simulated. See [selected interaction contract](selected-interactions.md) for behavior and validation limits.

## Alternatives to compare

| Workflow | Alternatives in Figma | Recommendation and reason |
|---|---|---|
| Explorer | [E1 — Token bar](https://www.figma.com/design/yAIbDoFO7bJd7Vm22nCc6p?node-id=34-3), [E2 — Filter panel](https://www.figma.com/design/yAIbDoFO7bJd7Vm22nCc6p?node-id=34-4) | E1: the approved catalog sidebar supplies navigation context; putting attribute filters over results avoids a second permanent left panel |
| Object | [A — Long document](https://www.figma.com/design/yAIbDoFO7bJd7Vm22nCc6p?node-id=34-5), [B — Workbench tabs](https://www.figma.com/design/yAIbDoFO7bJd7Vm22nCc6p?node-id=34-6), [C — IDE inspector](https://www.figma.com/design/yAIbDoFO7bJd7Vm22nCc6p?node-id=34-7) | B: balanced detail with a stable identity and explicit section destinations. A optimizes continuous scanning; C supports simultaneous SQL/relationships at the cost of width and panel management |
| Lineage | [L1 — Graph first](https://www.figma.com/design/yAIbDoFO7bJd7Vm22nCc6p?node-id=34-8), [L2 — List + graph](https://www.figma.com/design/yAIbDoFO7bJd7Vm22nCc6p?node-id=34-9), [L3 — Graph + inspector](https://www.figma.com/design/yAIbDoFO7bJd7Vm22nCc6p?node-id=34-10) | L3: retain focus evidence without opening another page. L1 prioritizes graph area; L2 prioritizes scanning the selected set and explicit keyboard-accessible object actions |

The table retains the original recommendations as decision history. The owner selected E1, Object C and L3, including Object C over the recommended B. Selection enables detailed interaction/state review; it does not make unstated URL additions, persistence or enhancements approved.

## Shared fixture and fields

Source is `site/public/data/catalog.json`, not production telemetry. Explorer filters Server is SQLPROD01 and Database is AppDb produce six matches; drawings show a labeled three-row excerpt, not a new three-row product cap. Every design preserves Object, Type, Server, Database, Schema, Description, Changes and Last changed. E2 deliberately illustrates a horizontally scrollable viewport rather than deleting columns.

Object example dbo.Orders retains qualified identity, server/database/schema, Customer orders description, three columns, current DDL, one dependency, two consumers, grants and historical/metric destinations. The three models differ in organization, not available data.

Lineage example focuses dbo.Orders at one hop with Type is StoredProcedures. Its four-node unfiltered neighborhood becomes two visible nodes: the focused table and dbo.usp_GetUserOrders. The retained edge carries Id and Total. This demonstrates why the type filter must not remove the focused table. It is not an assertion that only two objects exist or that every edge has column evidence.

## Explorer contracts for either arrangement

- Both structured attributes and DDL search remain visibly labeled, independent inputs. The filter panel, if selected, builds the same tokens; it must not replace negative/multivalue operators with simple checkbox facets.
- Keep AND across tokens, current case handling, commit-only attribute application, 150ms content debounce and search of DDL plus appended sections.
- Keep Explorer `filters` and `q` serialization, existing decode limits, malformed-state fallback and replacement of query history. Preserve unrelated parameters. No tree operation silently adds constraints.
- Seven sortable columns, initial name ascending, reversal on repeat selection and ascending on a new key; Description remains unsorted. Sorting is local today. Sorting persistence requires a specific approved change.
- Keep true match counts, first 500 sorted rows, full matching sorted CSV, all export fields, zero-result state and disabled empty export.
- Choosing E2 implies an explicit filter-panel toggle/focus specification at narrow widths. Neither arrangement approves hiding columns for visual cleanliness.

## Object capability placement by model

| Existing capability | A — Document | B — Tabs | C — IDE inspector |
|---|---|---|---|
| Identity/description/quick facts and exports | Persistent header + section anchors | Persistent header above tabs | Persistent header above workspace |
| Orphaned warnings | Header warning with full reference disclosure | Header warning, reachable regardless of active tab | Header warning, reachable regardless of inspector mode |
| System/linked/remote/dynamic reference evidence | Dedicated sections in document | Overview/reference sections, cross-linked from Lineage | Relationship inspector + full relationship workspace |
| Columns/descriptions/consumer panel | Columns anchor and expandable panel | Columns tab; incoming `column` deep link opens it | Columns workspace; `column` deep link opens relevant content |
| Current/historical DDL + appended sections | Definition anchor + revision banner | Definition tab + revision banner | Definition workspace + revision banner |
| Metrics and index selection | Metrics anchor | Metrics tab | Metrics workspace; inspector summary is not a replacement |
| GRANT/DENY, column scope and grantee links | Access anchor | Access tab | Access workspace; inspector summary is not a replacement |
| Revision selection/comparison and diff states | History → Definition/Diff anchors | History picker → Definition or comparison view | History picker → Definition or Diff workspace |
| Related objects/search/grouping/tag overflow | Depends on / Used by sections | Lineage tab | Relationship inspector plus full lists/workspace |
| Neighborhood/column graph and exports | Embedded graphs in relevant sections | Graphs in Lineage/Columns respectively | Graph workspace and column-specific workspace |
| CSV/XLSX and full Lineage navigation | Header + per-section actions | Header + per-tab actions | Header + relevant workspace actions |

Column C is the selected Object placement model. Its detailed interaction contract remains for review. B's Overview is object-level, distinct from the global Overview route. No reference type may be collapsed into a single ambiguous “issue” count. Counts/empty states must distinguish unavailable collection from confirmed zero where source data permits that distinction.

Historical DDL must remain labeled as historical while identity/metrics/grants remain current snapshot information. The workbook must still export the current object with its existing sheets even if a historical definition is displayed. Do not change that content by coupling exports to the visible tab. Revision selection, old-to-new ordering, third-pick replacement, missing DDL, identical diff and >2000-line handling all remain required.

## Lineage contracts for all models

- Preserve Browse and Access. In Browse, distinguish global filtering from focused-neighborhood filtering and keep the root. Preserve current clear-focus name-token behavior and breadcrumb stack truncation.
- Keep `filter`, `focus`, `hops`, `q` and Access `tab`/`grantee`. The entire focus stack and exact-match checkbox are not currently URL-persisted; no drawing changes that implicitly.
- Single-click on a real graph node still drills; double-click opens Object. L3's inspector follows focus rather than silently converting single-click into selection-only behavior. Edge inspection exposes all recorded columns; bundle/pane/close interactions remain.
- L2 lists the same selected neighborhood, not a second search of the entire catalog. Separate Focus and Open object actions make navigation intent explicit. This additional Browse list is proposed in PEP-09.
- L3 uses current catalog identity and relationship evidence in an inspector (PEP-10). Its close/reopen state and focus handling require specification after model choice.
- Preserve one/two/three hops, graph selection cap 300, grouping breakdown with narrowing in Browse only, existing bundling/member access, zoom/pan/minimap/fit, full labels and theme-aware SVG/PNG output.
- In Access retain grantee discovery/suggestions, substring/exact distinction, true grant counts, GRANT/DENY and column scope, links and one CSV row per permission. A graph/list/inspector arrangement must not imply computed effective access or transitive role expansion.

## Required states and interactions for the next specification pass

| Area | States to draw/specify after selecting the model | Verification |
|---|---|---|
| Shared shell/tree | Catalog loading/failure, long identities, many servers, collapsed/open tree, selected leaf, keyboard focus, unavailable AI, light/dark | All routes, focus return, lazy/bounded tree rendering; tree navigation must not create hidden filters |
| Explorer | Zero matches, 500/501+, multiple negative/list tokens, draft suggestion states, cleared/whitespace DDL query, empty export, long/wide values | Complete CSV count/schema; all operators, keyboard builder, sort and URL reload |
| Object | Not found, no columns/history/grants/metrics, no DDL, current vs historical banner, missing historical DDL, comparison picks, equal/oversized diff | Column deep links select visible workspace; object navigation resets existing revision state; exports preserve current-node semantics |
| References | Orphaned, system, remote not extracted, unused link, dynamic and incomplete column evidence | Explanations remain distinct and accessible in every model |
| Lineage | Global/focused scope, narrow neighborhood, invalid focus, empty selection, 300/301, bundled hub, expanded member/edge panel, grantee no results, exact selection | Root retained; hops/breadcrumbs; access permission rows; graph controls and export |
| Responsive/a11y | Desktop 1440/1280, constrained 1024, narrow 390, 200% zoom, keyboard-only and screen-reader paths | Collapse context first; table/code scroll retains content; tabs/drawers need roles and focus semantics; non-color status |

These edge states remain required. The selected interaction contract identifies the eight annotated state sheets and distinguishes them from specified-only states. Gate 4 is not closed until the owner approves the detailed contract. Figma review links are connected; full input and graph simulation is not claimed. No application dependency was added.

## Engineering boundaries and proposed defaults to review

- Keep models, API/catalog schema and extraction untouched. Reuse existing React components/helpers before introducing shared navigation primitives.
- Proposed tree default: branch activation expands/collapses only; leaf activation navigates to the existing Object route. Filtering occurs only in explicit filter controls. This is a proposal for the next review, not an inference from architecture approval.
- Proposed section navigation must preserve `column` links and unrelated parameters. Do not select an unapproved new `view`/revision URL schema while implementing a tab design. Exact state lifetime and return-context transport (PEP-01) remain explicit choices.
- Keep all 114 capability IDs in the migration matrix. Model selection will allow proposed-location cells to be populated, but “Required; migration unverified” remains until implementation evidence exists.
- Source Sans 3 and provisional Source Code Pro remain; light blue/neutral treatment carries Developer Tool emphasis without committing production tokens before Gate 5.

## Validation of this deliverable

All eight comparison frames are editable auto-layout Figma compositions on `04 — Wireframes`, with source-backed sample data. They are visually inspected for text/layout issues; this is not browser, keyboard, responsive or functional certification. Application and test code remain unchanged, so the previous 214-test baseline is retained without unnecessary reruns.
