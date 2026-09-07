# Remaining page compositions — Gate 5 review increment

> Current-state notice (2026-09-07): This document retains the earlier design/implementation record. Later owner-approved changes supersede its sidebar, Object inspector/Definition/Properties, graph, navigation and Alerts descriptions. Use [Current implemented UX](current-experience.md) and [ADR-0001](../adr/0001-sqlsync-investigation-workspaces-and-alerts.md) for the current contract. Original Figma frames and validation results remain historical evidence, not updated screenshots.

This increment was approved D-015. Later responsive, syntax, integrated-state and handoff refinements are recorded in [the final design review](final-design-review.md), which supersedes the design-work checklist at the end of this historical increment.

2026-09-06. D-014 accepts the previously presented Object/responsive/state extension. This increment carries forward the approved direction; it is not an approval of unpresented enhancements or a claim that Gate 5 is complete.

## Review links

| Artifact | Figma node |
|---|---|
| Overview, light desktop | [108:7](https://www.figma.com/design/yAIbDoFO7bJd7Vm22nCc6p?node-id=108-7) |
| AI unavailable | [108:428](https://www.figma.com/design/yAIbDoFO7bJd7Vm22nCc6p?node-id=108-428) |
| AI available, simulated result | [111:300](https://www.figma.com/design/yAIbDoFO7bJd7Vm22nCc6p?node-id=111-300) |
| History expanded / collapsed | [108:335](https://www.figma.com/design/yAIbDoFO7bJd7Vm22nCc6p?node-id=108-335) / [112:22](https://www.figma.com/design/yAIbDoFO7bJd7Vm22nCc6p?node-id=112-22) |
| Lineage Access | [115:21](https://www.figma.com/design/yAIbDoFO7bJd7Vm22nCc6p?node-id=115-21) |
| Explorer / Lineage at 1280 | [117:65](https://www.figma.com/design/yAIbDoFO7bJd7Vm22nCc6p?node-id=117-65) / [117:175](https://www.figma.com/design/yAIbDoFO7bJd7Vm22nCc6p?node-id=117-175) |
| AI runtime specimens | [114:14](https://www.figma.com/design/yAIbDoFO7bJd7Vm22nCc6p?node-id=114-14) onward on States page |
| Explorer / Lineage boundary specimens | [115:363](https://www.figma.com/design/yAIbDoFO7bJd7Vm22nCc6p?node-id=115-363) onward on States page |

## Screen and behavior contract

### Overview — orient and investigate

First five seconds: estate counts, static synchronization time, latest change and attention. Thirty seconds: 30-week activity and recent object changes. Deeper investigation: reference, change-frequency and co-change rankings. Existing object links navigate to the same Object route; help retains resolution and history semantics. No risk score or new collection is introduced.

Source fixture is `site/public/data/catalog.json`: 9 objects, 2 servers, 3 distinct server/database contexts (including `_ServerLevel`), 1 mined commit and 8 resolved edges. Latest change is REMOTE1 at 2026-08-20T18:49:14-03:00; catalog generated at 2026-08-20T21:49:26Z. Metrics extend to 21 August in the supplied fixture; retain this source inconsistency rather than changing timestamps.

Orders row-count alert: 55,800 to 106,020, 90%. Fragmentation: 7.5% to 68%, with previous displayed as 8% following current formatting. Heuristic thresholds remain in `anomalies.ts`; do not expose its internal magnitude as a new risk metric. Missing prior index fragmentation follows the existing fallback. At least two snapshots are required. No-anomaly wording and top-ten truncation remain subject to the separate, unapproved enhancement proposals.

Heatmap values count **commits touching a type**, not affected objects. Three tables in the one commit count once in the Tables row. At the 6 September review date, the 30 Sunday-starting weeks run from 15 February to 6 September; the active cell is week 16 August. Every active type totals 1; Functions totals 0. Intensity uses each row's maximum. Type colors remain the existing palette, now represented by six additional semantic evidence-color variables; labels supply non-color meaning. Preserve every week's tooltip/value and total in implementation.

Recent/most-changed lists contain all seven eligible fixture objects, capped at ten by existing helpers. Users has 4 direct and 4 reachable incoming users; Orders has 2 and 2. Indirect includes direct users, excludes the root and uses existing six-hop/cross-server stopping rules. Co-change preserves the first ten source pairs, count 1 each. Preserve missing-ID fallback, both object links, rank intensity and empty messages.

### AI — local filter planning

AI remains a primary destination. The unavailable frame represents model-missing; other reason-specific copy remains as implemented: LFS unresolved, checksum mismatch, packaging failure, runtime failure and default/manifest unavailable. Retry is available only for runtime-error, with actual error details. Do not invent Retry on missing deployment assets.

The available frame is a **simulated plan**, not evidence of a model run. It uses Database=AppDb, Type=StoredProcedures and DDL content Orders; 1 of 9 objects match. Confidence stays high/medium/low, not a probability. Chips are read-only preview text. The existing count-only preview is retained; there is no new object-result list.

Examples fill the prompt without submitting. Empty/busy disables Generate. Loading/running disables prompt and examples; Cancel aborts. A new request clears the old plan/error. Progress shows the supplied filename and percent only when available; otherwise use an indeterminate state. Abort is not presented as an inference error. Warnings remain visible and do not alone block a valid plan; unsupported fragments block Explorer until rewritten. No tokens plus no content query also blocks Explorer.

Open in Explorer uses the existing `filters` serialization and `q`; no URL vocabulary changes. Interpretation/filtering stay in the browser; model asset loading is not a promise of zero network activity. Runtime specimens are annotations for implementation, not a working local model prototype.

### History — inspect a commit

Preserve `recentChanges` chronology, bounded-window explanation, timestamp, short SHA, full SHA, message and source affected-ID count. Expanded fixture contains all seven known affected objects in source order, with type and context. Unknown IDs remain omitted from links without altering the commit's source count. Each SHA expands independently; keyboard activation exposes `aria-expanded` and keeps focus on the trigger. The two Figma frames demonstrate expansion/collapse only, with no new route state, filter or export.

### Access — investigate extracted permissions

The fixture searches app_reader: 3 grants across Orders, Users and usp_GetUserOrders. Display one object row and one CSV row per matching permission. The graph contains their three existing relationships and existing column evidence. Inspector distinguishes matched-principal evidence from other grants already on the selected object. All values come from current catalog grants; no effective-permission inference is introduced.

Retain case-insensitive substring/exact matching, user/role/group suggestions (20 displayed), 120 ms debounce and source URL replacement. Exact checkbox remains local, not silently serialized. Grantee selection/search clears focus as it does today. Browse/Access mode switching retains the approved source state-reset contract. Column DENY, absent grantee type and missing grants remain distinct states. Empty query and no matches have different messages. Above 300 objects, preserve the existing breakdown and +N remaining: Browse groups narrow; Access summaries do not.

## Component mapping and accessibility

| Design composition | Engineering mapping |
|---|---|
| Shell and catalog context | Approved AppShell/navigation refactor; existing routing contract |
| Summary, rankings and activity | Home page composition around existing analytics and TypeActivityHeatmap |
| AI prompt/status/preview | AiPage composition; existing AiContext, planner and filter helpers |
| Commit row/disclosure | History composition; reusable disclosure primitive |
| Access input/table/export | Refactor existing LineagePage controls, grants helpers and CsvExportButton |
| Graph and inspector | Existing LineageGraph behavior plus approved reusable InspectorPanel |
| Buttons/chips/input/status | Existing Figma families; reusable React primitives in later implementation |

Use labeled form controls, visible focus, actual table headers, `aria-sort` for sortable headers, semantic disclosure buttons, status announcements and real progress semantics. Graph export and gesture behavior remain existing contracts; static vectors are not substitute implementations. Readable graph alternatives must retain relationship/column information. Drawer dismissal restores trigger focus. Implementation must validate keyboard interaction, contrast, zoom and large data before claiming WCAG parity.

At 1280 the catalog collapses behind an explicit entry while dense results and inspector content remain. Existing Object examples cover 1024 and 390; full Explorer/Lineage narrow viewport and 200% zoom designs/checks remain open. Tables and code must keep all fields through horizontal scrolling, not remove columns.

## Validation and open work

Screenshots reviewed: Overview, both AI desktop states, History expanded, Access, Explorer/Lineage 1280 and representative state specimens. Corrected heatmap units/counts, source sort order, unchanged function metadata (0 changes and no last-change timestamp), Access arrow placement and read-only/count-only AI preview. History expansion/collapse has two linked frames. Other controls are specifications unless explicitly linked; screenshots do not test URLs, exports, AI, graph gestures or runtime performance.

No app code or dependencies changed; prior baseline remains 214 passing tests across 27 files. Do not rerun tests solely for these design/doc changes or claim fresh results. Remaining Gate 5 work includes complete component families/variants, integrated edge states, narrow viewport coverage, syntax colors, type treatment across graphs, graph minimap/details, full prototype linkage and final handoff/visual QA. Existing object-workspaces.md limitations still apply. Broad implementation starts only after final Gate 5 approval.
