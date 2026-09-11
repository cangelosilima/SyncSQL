# Object workspaces and responsive design — Gate 5 extension

> Current-state notice (2026-09-07): This document retains the earlier design/implementation record. Later owner-approved changes supersede its sidebar, Object inspector/Definition/Properties, graph, navigation and Alerts descriptions. Use [Current implemented UX](current-experience.md) and [ADR-0001](../adr/0001-sqlsync-investigation-workspaces-and-alerts.md) for the current contract. Original Figma frames and validation results remain historical evidence, not updated screenshots.

The owner approved the initial desktop compositions/foundations in D-013. This pass extends that treatment without changing production code. It does not close the remaining Gate 5 coverage or imply that prototype links execute database operations.

## Figma views

| Workspace | View | Data and behavior preserved |
|---|---|---|
| Columns | [Expanded Total](https://www.figma.com/design/yAIbDoFO7bJd7Vm22nCc6p?node-id=90-30), [closed disclosure](https://www.figma.com/design/yAIbDoFO7bJd7Vm22nCc6p?node-id=100-135) | Three real columns, types, nullable descriptions, consumer evidence, CSV and best-effort explanation |
| Access | [Grant rows](https://www.figma.com/design/yAIbDoFO7bJd7Vm22nCc6p?node-id=90-93) | Both app_reader/app_writer entries; grantee type, permission, GRANT and whole-object scope |
| Metrics | [All current SQL Server trend families](https://www.figma.com/design/yAIbDoFO7bJd7Vm22nCc6p?node-id=90-156) | Row count; reserved/data/index KB; fragmentation; seeks/scans/lookups/updates; pending modifications; latest statistic fields |
| History | [Revision navigation](https://www.figma.com/design/yAIbDoFO7bJd7Vm22nCc6p?node-id=90-219) | Full source SHA, timestamp/offset, message, Current and comparison entry |
| Definition revision | [Historical DDL](https://www.figma.com/design/yAIbDoFO7bJd7Vm22nCc6p?node-id=99-149) | Historical banner, current inspector metadata, Back to latest; additional definition sections hidden in historical view |
| Diff | [Identical definitions](https://www.figma.com/design/yAIbDoFO7bJd7Vm22nCc6p?node-id=90-282) | Real current/history pair is identical; no invented additions or removals |
| Relationships | [Lists and neighborhood](https://www.figma.com/design/yAIbDoFO7bJd7Vm22nCc6p?node-id=90-345) | One dependency, two consumers; four nodes and all five internal edges; direction, export controls and minimap affordance |

The source fixture is site/public/data/catalog.json. Fourteen metrics snapshots span 8–21 August 2026; the fixture's metrics dates can exceed its catalog generatedAt date. Preserve those source timestamps rather than silently correcting them. Latest table rows (106,020) and optimizer-statistics rows (56,450) are different measurements. No new risk score, effective permission computation, extracted field or AI inference was introduced.

Charts plot all 14 source points. Axis maxima are visual scales, not new limits. Size and index-usage series use distinct line patterns as well as color. Browser implementation must retain source chart formatting, null gaps, hover values, index selection and Oracle rows/leaf-block variants. The selected Orders fixture does not exercise Oracle or nullable series.

## Responsive layouts

| View | Behavior |
|---|---|
| [1280 desktop](https://www.figma.com/design/yAIbDoFO7bJd7Vm22nCc6p?node-id=97-2) | Catalog collapsed; SQL and inspector stay side by side |
| [1024 constrained desktop](https://www.figma.com/design/yAIbDoFO7bJd7Vm22nCc6p?node-id=97-63) | Catalog and inspector collapsed; explicit inspector opener retains workspace |
| [390 narrow](https://www.figma.com/design/yAIbDoFO7bJd7Vm22nCc6p?node-id=97-263) | Navigation/catalog controls, workspace menu, horizontally scrollable SQL and export menu |
| [390 inspector](https://www.figma.com/design/yAIbDoFO7bJd7Vm22nCc6p?node-id=98-37) | Current relationship evidence, explicit close, no implicit focus change |
| [Workspace menu](https://www.figma.com/design/yAIbDoFO7bJd7Vm22nCc6p?node-id=98-48) | All seven Object destinations retained |
| [Navigation menu](https://www.figma.com/design/yAIbDoFO7bJd7Vm22nCc6p?node-id=98-64) | All five global destinations, AI availability retained |
| [Exports menu](https://www.figma.com/design/yAIbDoFO7bJd7Vm22nCc6p?node-id=98-76) | Existing CSV scopes and current-object XLSX remain explicit |

These widths are concrete review examples. Final CSS breakpoints require content-based validation, particularly long localized text, 200% zoom and Explorer/Lineage narrow layouts. Keyboard drawer requirements: labeled region/dialog; modal focus trap only when modal; Escape closes topmost overlay; focus returns to the opener; closing does not clear focus/filter/revision. Menus must expose selection and disabled states with semantics, not color alone.

## States and edge cases

[State specimens](https://www.figma.com/design/yAIbDoFO7bJd7Vm22nCc6p?node-id=101-2) cover catalog loading/error, object not found, no columns/history/grants/metrics, missing historical DDL, orphaned/system/remote references and oversized diff. Synthetic fixtures are labeled; actual reference specimens use dbo.ArchivedOrders, sp_executesql and the SQL_A remote function from the catalog. These are visual fragments with explanatory annotations, not final integrated screen copies or final product copy.

Do not infer generic retry capability from these states. Preserve the actual catalog error and publication explanation. Missing historical DDL stays disabled. Loading does not show invented percentages. No extracted grant is not proof of no effective access.

## Prototype and QA limits

Object workspace switches, a fixed comparison example, historical return, column disclosure and narrow inspector/menu return paths are connected. Self-navigation was omitted because Figma rejects NAVIGATE to the same containing frame. These links review layout and context, not real revision-pick logic, filtering, exports or graph gestures. Menus expose available actions; not every item has a destination simulation. No screen-reader or browser functional certification is claimed.

Screenshots inspected: all six added Object workspace types; historical Definition; closed/expanded column states; 1280, 1024, 390 and narrow inspector; all twelve state specimens; source trend charts. Visual corrections included plot coordinate offsets, endpoint date alignment and canvas spacing as the Metrics workspace grew. Metrics and relationship source field/edge checks were read-only. Application and test code remain unchanged; the existing 214-test baseline is still the latest test execution.

Remaining design coverage: Oracle/nullable metrics examples, high-fidelity grouped/bundled and column-graph states, non-identical diff, SQL syntax colors, additional component families, Overview/AI/History, Explorer/Lineage responsive layouts and final engineering handoff. Preserve all corresponding parity inventory rows while those artifacts are completed.
