# SQLSync approved UI implementation

> Current-state notice (2026-09-07): This document retains the earlier design/implementation record. Later owner-approved changes supersede its sidebar, Object inspector/Definition/Properties, graph, navigation and Alerts descriptions. Use [Current implemented UX](current-experience.md) and [ADR-0001](../adr/0001-sqlsync-investigation-workspaces-and-alerts.md) for the current contract. Original Figma frames and validation results remain historical evidence, not updated screenshots.

Date: 2026-09-07. Authorization: D-016 Gate 5 approval and the product owner's “Implement all” instruction. All eight approved UI phases are implemented. Optional product/data enrichment remains separate.

## Delivered compositions

| Phase | Implementation | Functional boundary |
|---|---|---|
| 1 — Foundations | Semantic light/dark tokens, focus styles, shared Button, export states and code palette | Saved theme and local storage fallback retained; light is the default |
| 2 — Shell | Five primary destinations, snapshot timestamp and all server names, collapsible Catalog context, skip link | Hash routes unchanged; tree branches only expand; leaves use existing Object URLs; 100-item branch pages expose every sibling |
| 3 — Explorer | Token bar, labeled DDL search, matching count, dense eight-field table, accessible sorting | Existing grammar, debounce, filters/q and seven sort comparators; 500 visible rows; complete sorted CSV |
| 4 — Object | Definition, Columns, Graph, Access, Metrics, History and Diff; Relationships/Properties inspector | Full source sections, orphan warning, current-object exports, column links, comparison ordering/replacement and historical SQL retained |
| 5 — Lineage | Graph plus focus/edge inspector, Browse/Access controls and keyboard grantee suggestions | Scope/root semantics, focus stack, hops, exact matching, caps, bundles and permission CSV retained |
| 6 — Overview | Estate summary, paired attention sections, full-width activity, dense investigation rankings | Existing counts, thresholds, mined windows, heatmap units and ranking limits retained |
| 7 — AI | Separate prompt/runtime/preview composition with all availability states | Local model, cancellation, confidence/warnings, unsupported-fragment gate and filters/q transfer retained |
| 8 — History | Commit disclosure, full SHA, object context, accessible expansion | Source chronology, timestamps, affected-ID counts and unknown-ID behavior retained |

The migration reuses FilterBar, RelatedObjects, CodeBlock, DiffView, MetricsPanels, TypeActivityHeatmap, graph layout and export generators. No package, schema, extraction, backend, model or analytics changes were made. The existing untracked NuGet.Config is untouched.

## Validation

The pre-implementation baseline passed 222 tests across 29 files. New regression coverage exercises Explorer URL/filter composition and the 500/501 CSV boundary; revision and comparison state across workspaces; keyboard tabs and inspector focus return; Access exact matching, keyboard suggestions and per-permission CSV; graph evidence and live-graph export dispatch; AI cancellation, progress, unavailable/error states and actual Explorer transfer; History disclosure; and bounded catalog-tree rendering without filter changes.

Existing Object assertions were retained and now open Columns or Graph before inspecting the approved workspace. No assertion, test timeout or dependency was removed or relaxed. Two full runs encountered intermittent 5-second packaging subprocess timeouts; the isolated eight-test packaging suite subsequently passed. The final full suite passed **237 tests across 36 files** in 35.58 seconds with two workers. Output is in latest-tests.log.

TypeScript compilation, Vite production build and AI model packaging verification passed. Final application bundle: approximately 607.4 kB / 200.4 kB gzip; stylesheet 59.2 kB / 10.8 kB gzip. The previous Phase 1 application bundle was approximately 595 kB. Existing lazy workbook and AI worker/WASM chunks remain separate. Graph caps, filtering debounces and visible-row limits are unchanged; the Object neighborhood graph mounts only in Graph. No large-estate responsiveness benchmark is claimed.

### Browser evidence

- Compared the original HEAD app and implementation using the same nine-object catalog. Reviewed original Explorer, Object, Lineage, Overview, AI-unavailable and History screenshots, then the approved Figma compositions and implementation. Temporary baseline source/server were removed after comparison.
- Reviewed implementation at 1440×900, 1280×720, 1024×768 and 390×844 across the core layouts. These were representative checks, not every screen/state in every size/theme combination. Object and History narrow layouts had no document-level horizontal overflow; wide technical tables/code retain their own scroll containers.
- Verified light and dark appearances, persisted theme on reload, native narrow inspector dialog, Escape dismissal and focus return. Desktop catalog context defaults open above 1280; Object/Lineage inspectors above 1024. Crossing a breakpoint closes the affected panel; explicit reopening remains available.
- Ran the real packaged local model in production preview. “Show stored procedures in AppDb that mention Orders” generated Database=AppDb, Type=StoredProcedures and DDL=Orders, with high confidence and 1 of 9 matches. Explorer received the exact ordinary filters/q state and the same matching count.
- Followed the matching procedure into Object and full Lineage. Selected the recorded procedure→Orders edge; both graph and inspector exposed Id and Total. Existing navigation, focused-neighborhood explanation and radius controls remained present.
- Reviewed expanded dark History with seven affected objects, full SHA and qualified server/database/schema context.

### Visual adaptations and remaining certification limits

The implementation is responsive CSS and real data rather than fixed Figma frames. The source graph engine, layout, minimap, controls and complete evidence panels are reused. Edge evidence is mirrored in the inspector while retaining the graph panel so it remains reachable when the inspector is closed. Catalog branches initially collapse rather than hardcoding the expanded fixture path. Full technical tables retain horizontal scrolling. Source monospace fallbacks are retained without adding a font package.

Figma's sampled rows and abbreviated relationship lists do not limit runtime contents. Default SQL remains dark in both themes. Type badges use readable foreground text with type-colored borders; focused graph nodes use the selected surface and retain type borders.

The in-app browser's download-event capture timed out for a generated SVG, with no reported browser error. Completed native file downloads are therefore not certified by that browser check. CSV/XLSX contents and graph export dispatch are covered by the automated suites. Full screen-reader, cross-browser and 200% zoom certification remain outside this completed implementation check; WCAG conformance is not claimed solely from these tests.

The 114-row Functional Parity Inventory records current locations and scoped evidence. “Retained” is a source and behavioral-contract assessment, not a claim of exhaustive testing over every possible catalog.

## Review locally

Production preview: http://127.0.0.1:5189/ . The packaged model is available there; the development server can correctly show model-unavailable when its generated capability manifest is absent.

Run from site:

```text
node node_modules/vitest/vitest.mjs run --configLoader runner --reporter=dot --maxWorkers=2
node node_modules/typescript/bin/tsc -b
node node_modules/vite/bin/vite.js build --configLoader runner
node scripts/package-ai-model.mjs
```

Changes are local and uncommitted. No deployment was performed.
