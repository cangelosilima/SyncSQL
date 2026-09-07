# Latest validation — 2026-09-07

Full suite rerun for this documentation update: **253 tests passed across 38 files**, 38.32 seconds, two workers. See [latest-tests.log](latest-tests.log). Run from `site` using `node node_modules/vitest/vitest.mjs run --configLoader runner --reporter=dot --maxWorkers=2`.

Latest implementation builds passed TypeScript, Vite and AI packaging verification. Current app bundle is approximately 616.5 kB / 203.2 kB gzip and stylesheet 61.7 kB / 11.2 kB gzip. No new graph/test dependency or backend contract was introduced.

New/updated coverage includes column-reference intent and ambiguous targets, persistent Object definition/revisions, Relationships tab order, persistent Lineage inspector, catalog scope/branches, directional layout sizes, Alerts full-result paging/URL filtering and Alerts help registration. Updated dismissal/Properties assertions follow explicit owner interaction changes.

Browser checks are scoped samples, not complete certification: graph frames/handles, metrics table containment, exact AI prompt/recorded consumer preview, persistent Definition and Alerts rendering were inspected. Original Figma comparison and breakpoint evidence below predate some later changes. Native download completion, full accessibility/cross-browser/zoom coverage and large-estate performance remain unverified. Figma was not modified in this documentation pass.

## Historical validation plan and baseline

# Validation — Implementation update

The approved migration is implemented. Current commands, regression evidence, real local-AI verification and browser limits are recorded in [implementation-complete.md](implementation-complete.md); final test results are in latest-tests.log. The discovery baseline and planned checks below are historical.

## Executed baseline

2026-09-06, source `e9c1683e214917e1da3dd71e3073ed5241aa4720`:

```
cd site
node node_modules/vitest/vitest.mjs run --configLoader runner --reporter=dot
```

Result: **27 test files passed, 214 tests passed**, Vitest 3.2.7, approximately 16 seconds. The ordinary npm launcher failed because its npm-cli.js target was missing; the installed Vitest runner completed directly. No test dependency was added. This result is frontend-only, not a CLI test or production-build result.

Browser inspection covered Overview, Explorer, dbo.Orders Object, focused Lineage, History, and unavailable AI at 1280×720. The local deployment does not advertise a usable AI model; actual model loading/inference was not manually verified. Component mocks and helper tests do not substitute for real graph/runtime/download checks.

## Existing protection and gaps

| Area | Existing evidence | Additional behavioral coverage before migrating it |
|---|---|---|
| Filtering/search | filters/contentSearch helpers and focused-Lineage tests | Real FilterBar keyboard/multivalue interaction; Explorer filters/q reload; malformed URLs; appended-section search; debounce behavior |
| Explorer | Helper tests only; no Explorer page test file | Every sort key/direction; nulls; 500/501 rows; all matching sorted CSV rows and headers; zero results |
| Object | Linked references, column panel, system-reference distinction, workbook affordance | Revision picker/current/old-to-new ordering; third-pick replacement; unavailable/identical/>2000-line diff; object-change state reset |
| Lineage | Focus retention, neighborhood narrowing, clear-focus name token | 1/2/3 hops, focus breadcrumbs, mode changes, URL reload and browser history behavior, 300/301 selection breakdown |
| Access | Grant helpers | Suggestions, exact vs substring, GRANT/DENY/column scope, one CSV row per permission, missing grantee and empty results |
| Graph | Neighborhood/layout-related helpers; graph is mocked in page tests | Real click/double-click, pan/zoom, bundle and edge panels, close/pane dismissal, SVG/PNG content and theme |
| AI | Planner, runtime failure/retry, capability parsing, shell availability | Full page preview → Explorer, unsupported fragments block handoff, cancel/unmount, known/unknown progress, every deployment reason |
| History/Overview | Analytics helpers | Commit expansion and missing objects; null/empty status; capped lists; precise count units and date windows |
| Shared/export | CSV/XLSX workbook/utilities; help tests | Busy/error/retry XLSX; zero CSV; exact headers/sheets and long DDL cell splitting; focus trap/return, aria-sort and expansion |

Add tests within existing Vitest and Testing Library first. A new E2E dependency requires a separate proposal and approval; none is approved or installed here.

## Representative validation fixtures (planned, not executed)

- Keep the nine-object bundled sample as the small baseline with real schema-shaped SQL and references.
- Add deterministic, explicitly synthetic fixtures for 10,000 and 50,000 objects; separately vary edge count and hub fan-out to 100 and 1,000 neighbors. These are test targets, not claimed production measurements.
- Exercise 0/1/500/501 Explorer matches; graph 29/30/31, 59/60/61, and 299/300/301; dependency list 12/13 and group 25/26; diff 1999/2000/2001 lines.
- Include duplicate qualified names across servers, slash IDs, null optional fields, absent legacy reference arrays, cycles/self-references, dynamic edges, unresolved remote targets, MSSQL and Oracle metric shapes.
- Exercise 0/1/2/many metric snapshots with null gaps; no history and missing historical DDL; missing current objects in commits; AI all capability reasons and cancellation.

## Performance and visual acceptance

Before each migration, record the same browser/device and fixtures for filter latency, graph preparation/layout, object rendering, revision diff, export duration and browser memory. Use before/after comparisons; do not invent a numeric SLA without baseline measurements. Preserve debounces, memoization, lazy workbook/model loading and existing safety limits unless separately approved.

Capture BEFORE → APPROVED FIGMA → IMPLEMENTATION at 1440×900, 1280×720 and 1024×768 desktop/tablet widths, plus 390×844 for fallback behavior. Check light/dark, long names, horizontal overflow, zoom/text scaling, keyboard/focus, tables, dialogs and color-independent status. No mobile-first simplification may discard desktop capabilities.

For each migrated capability ID record its approved destination/node, intentional interaction delta, test/manual evidence, and parity result. “Required” becomes “Verified” only after that evidence exists. Document every justified Figma deviation. Each phase must leave the application reviewable and tests passing; do not delete behavioral assertions to accommodate new markup.
