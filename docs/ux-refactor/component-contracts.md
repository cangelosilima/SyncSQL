# SQLSync component contracts

> Current-state notice (2026-09-07): This document retains the earlier design/implementation record. Later owner-approved changes supersede its sidebar, Object inspector/Definition/Properties, graph, navigation and Alerts descriptions. Use [Current implemented UX](current-experience.md) and [ADR-0001](../adr/0001-sqlsync-investigation-workspaces-and-alerts.md) for the current contract. Original Figma frames and validation results remain historical evidence, not updated screenshots.

Gate 5 design specification. These are implementation contracts, not claims that every composition is already a published Figma component or implemented React primitive. The five approved Figma families are Button, Input, FilterChip, StatusBadge and WorkspaceNav. Compose the following from those foundations and the existing technical components; do not replace working domain behavior with decorative replicas.

## Shared rules

Light is the default; preserve explicit stored theme choice and the dark option. Semantic variables control application surfaces, text, status and focus. Preserve Source Sans 3; measure the existing platform monospace stack against the Source Code Pro design reference before changing font delivery. Use 4/8/12/16/24/32 spacing and 4/8 radii. No ornamental animation or shadows are required. Desktop engineering workflows take priority.

Dense rows target 32px but grow for wrapping content; never truncate an entire field without access to its full value. Balanced metadata uses 14/20 body and 16–24px section spacing. Controls use 36px height by default and at least 24px target size. Focus is a visible 2px outline with offset; do not use selected fill alone as focus. Normal, hover, focus, selected, disabled and loading are independent meanings. Native disabled controls remain noninteractive. Preserve selection when temporarily opening a disclosure or inspector.

## Composition and state catalogue

| Element | Mapping and content | States and interaction contract |
|---|---|---|
| AppShell | Refactor existing App; shared composition | Static snapshot loading/error/ready; default light and explicit dark; skip link to main; no live-health claim |
| Navigation | Refactor route links; approved context sidebar | Current route uses text/shape cue and aria-current; collapse/expand with focus return; five existing destinations remain; AI availability is separate from current route |
| Command/Search | Existing structured and DDL search only | No command palette or new global-search behavior; those remain proposals |
| Breadcrumb | Existing Lineage focus stack and qualified context | Current position is text; existing ancestors activate existing navigation; do not serialize the whole local stack or change browser history semantics |
| PageHeader | New reusable composition | Title, context, help, actions; actions wrap before information disappears; long qualified names wrap |
| ObjectIdentity | New reusable composition of current fields | Qualified name, type, server/database/schema and description; preserve absent schema and description; never infer owner/health |
| StatusBadge | Existing Figma family; reusable primitive | Normal/warning/error/unavailable and loading labels; neutral unavailable differs from error; text remains readable without color |
| TypeBadge | Reuse TypeBadge.tsx | Existing type vocabulary, palette and fallback; colors are accents with readable labels; distinguish database links and linked servers by actual type |
| Metric | Composition around source values | Unit, timestamp, null/unavailable and precise value; no null-to-zero conversion; deterministic metric versus heuristic explicitly described |
| Alert | Reusable composition | Warning/error/anomaly/orphan with source explanation; no generic Risk label; only show retry when source operation supports it |
| FilterBuilder | Refactor FilterBar.tsx | Attribute/operator/value stages, suggestions, negation and multiple tokens; preserve committed versus draft state and current semantics; labeled combobox, active option and Escape behavior |
| FilterChip | Existing Figma family | Read-only in AI preview; removable in actual filter builder with named remove button; removal updates only existing URL state |
| SearchInput | Refactor ContentSearchBar / grantee input | Visible label, clear affordance, busy/disabled/focus/error as applicable; retain debounce, appended-DDL search and current counts |
| DataTable | Reusable visual wrapper around page tables | Semantic headers, all fields, long values and horizontal scroll; preserve row caps independently from full export; no new row selection state implied by hover styling |
| SortHeader | Refactor Explorer sort button | Ascending/descending and aria-sort on th; seven sortable columns; Description remains unsortable; preserve numeric/date/null ordering |
| EmptyState | Reusable composition | Empty catalog, no matches, no history, no columns/grants/metrics use distinct source explanations; keep filters visible for no matches |
| LoadingState | Reusable composition | Status announcement; indeterminate unless progress is supplied; avoid skeletons implying known data or arbitrary percent |
| ErrorState | Reusable composition | Preserve actual details and source retry path; unknown/missing/partial data are not automatically runtime errors |
| CodeViewer | Reuse CodeBlock.tsx / highlight.js | Current SQL, empty definition, missing historical SQL and appended sections; horizontal scrolling/selectable text; keep parser and syntax semantics |
| DiffViewer | Reuse DiffView.tsx / diffLines | Side-by-side old/new, added/removed/unchanged, line numbers and blanks; identical and oversized guards; no syntax highlighting required in diff (source is plain text) |
| MetadataList | Reusable composition | Definition list semantics, units and absent values; no invented replacement values |
| PropertyGrid | Reusable composition | Balanced labeled values; stack at narrow widths; preserve access to complete strings and technical terms |
| Tabs / WorkspaceNav | Existing Figma family; reusable switcher | Object local workspace state; semantic tablist/tab/tabpanel, keyboard arrows/Home/End and visible focus; honor column deep-link workspace activation; no new query parameter |
| Accordion | Refactor History / RelatedObjects disclosure | Button with aria-expanded/controls; independent expansion where source supports it; Enter/Space and focus retained; keep caps and +N remaining |
| Drawer | New reusable responsive primitive | Label, Close, Escape and return focus; modal trap only for blocking overlay; nonmodal desktop inspector does not trap focus |
| InspectorPanel | New composition around current object/edge fields | Open/closed/current selection; closing preserves focus/filter/hops; distinguish selected evidence from graph focus; object links keep existing routes |
| GraphToolbar | Refactor existing graph controls | Distinct zoom out/in, fit, SVG and PNG actions, available/disabled states; retain minimap and graph pan/zoom; no new engine |
| GraphNode | Reuse LineageGraph behavior | Type, focus/selection, regular/summarized and dynamic evidence; click drills down, double-click opens Object; groups remain groups rather than fabricated objects |
| GraphLegend | Reuse relationship/type explanations | Arrow direction and known column evidence; dynamic, orphan, system and external reference semantics remain distinct; labels supplement colors |
| RevisionSelector | Refactor Object revision controls | Current/historical, selection order, two-selection/third-pick behavior, missing historical SQL, Back to latest; version state lifetime remains source-defined |
| PermissionBadge | StatusBadge semantic composition | GRANT versus DENY, exact permission and optional column, grantee/type; never imply effective access or inherited role resolution |
| ExportMenu | Composition of CsvExportButton / XlsxExportButton | Scope-specific labels; complete matching data; workbook idle/busy/error/retry; preserve actual sheets, headers, filenames and long-DDL splitting |
| Help affordance | Reuse HelpButton.tsx and source markdown | Named trigger, dialog label, Escape/close/focus return; preserve every semantic explanation, correcting only audited contradictions |
| Tooltip | Shared presentational primitive | Available on focus and hover, dismissible with Escape and persistent while hovered; essential values also readable outside pointer-only tooltip |

## Color and code mapping

Object-type variables preserve source palette values. Labels use ordinary readable foregrounds, not raw orange/yellow type colors as small text. Syntax specimens keep a dark code surface in both application themes: background #282a36, text #f8f8f2, keyword #ff79c6, literal/type #8be9fd, string #f1fa8c and number #bd93f9. Comment foreground is proposed as #a1acc8 to improve contrast over the current #6272a4. Preserve remaining highlight.js categories; do not implement a regex-based SQL highlighter from the Figma sample.

Added/removed diff cues combine background with explicit +/− or equivalent accessible labels. Unchanged lines retain neutral foreground and both revision labels. Do not introduce difference coloring to the real identical fixture.

## Responsive and keyboard handoff

At 1440, show context plus workspace and inspector where space permits. At 1280, collapse context first. At 1024, move the inspector behind a clear trigger while keeping main content usable. At 390, stack controls, retain full table/code overflow and expose the inspector as a labeled view/drawer. Narrow graph layout may reflow node positions without changing edges; the two-node specimen keeps the focus in view. Do not disable pan/zoom or remove detail navigation because double-click is awkward on touch: inspector Open object remains available.

The 390 frames describe available CSS width, not a separate mobile feature subset. At 200% zoom, layout should respond to the reduced CSS viewport; verify in the browser after implementation. Figma width examples cannot certify actual zoom or screen-reader behavior.

Focus order: navigation → page title/help/actions → filter/search controls → results/workspace → inspector when open. Dialogs announce title and return focus. Status changes use polite live regions except actionable errors where appropriate; avoid announcing every keystroke twice. Scroll regions need accessible names and keyboard access. Every graph-only value needs an equivalent accessible description or existing relationship disclosure, without adding new analytics.
