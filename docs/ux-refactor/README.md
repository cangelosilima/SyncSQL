# SQLSync UX refactoring — implemented

Updated: 2026-09-11. The approved incremental refactor and subsequent owner-requested changes are implemented locally. Documentation reflects the current code; no deployment or Figma update is claimed. [Current implemented UX](current-experience.md) records what changed after 2026-09-07 and supersedes every older record in this folder on those points.

## Start here

- [Current implemented UX](current-experience.md): authoritative current screen, navigation, interaction and data contract.
- [ADR-0001](../adr/0001-sqlsync-investigation-workspaces-and-alerts.md): accepted architectural decisions, alternatives, consequences and limits.
- [Design decisions](design-decisions.md): original approvals and D-017–D-025 follow-up decisions.
- [Functional parity inventory](functional-parity-inventory.md): original 114 capabilities with explicit current-location overrides and approved removals.
- [Validation](validation.md) and [test output](latest-tests.log): the current site suite is 336 passing tests across 48 files (2026-09-11); `latest-tests.log` is the 2026-09-07 run it supersedes, and the certification limits recorded there still stand.
- [Enhancement proposals](enhancement-proposals.md): specifically authorized Alerts scope and remaining proposals.

## Current experience

Primary navigation is **Overview → Explorer → Lineage → Alerts → AI → History**. Catalog is always rendered only on Explorer/Object. Object explorer keeps Definition - now with an Original/Formatted toggle - above Columns, Graph, Access, Relationships, Metrics, History and Diff. There is no Object inspector or duplicate Properties footer. Explorer and Lineage share one chip filter bar in which DDL content and grantee are ordinary attributes, so Lineage has no Browse/Access tabs; it retains a permanent inspector, directional hop controls with optional layer grouping, and a compact legend docked below the graph that exported images always include. React Flow/Dagre remains the graph stack, with corrected handles, dimensions, spacing and direction arrows. The application is light-only; the theme toggle was removed.

Alerts consolidates all existing anomaly/orphan findings with shareable category/search filters and investigation links. AI now routes supported exact column-reference requests to the existing column workspace rather than literal DDL search. Overview retains its previews and uses separate summary cards; the extra Alerts shortcut was removed.

## Historical design and implementation record

The following documents preserve the discovery, approval and migration evidence. Their current-state notices identify superseded contracts; do not implement old dismissal controls or Definition/Properties placements from those records.

- [Information architecture alternatives](information-architecture.md), [visual directions](visual-directions.md), [core workflows](core-workflows.md)
- [Selected interactions](selected-interactions.md), [Object workspaces](object-workspaces.md), [remaining pages](remaining-pages.md)
- [Design system](design-system.md), [component contracts](component-contracts.md)
- [Final design review](final-design-review.md), [engineering handoff](engineering-handoff.md)
- [Phase 1](implementation-phase-1.md), [original completion report](implementation-complete.md), [follow-up summary](2026-09-07-follow-up-fixes.md)
- [Approved Figma file](https://www.figma.com/design/yAIbDoFO7bJd7Vm22nCc6p): earlier approved frames, not a claim that later code changes are synchronized.

The existing `*-state.json` and progress JSON files are design-stage snapshots. The current Markdown contract supersedes them for post-implementation decisions; their Figma node references have not been fabricated or refreshed.

## Review locally

Preview: http://127.0.0.1:5189/ . Run checks from `site`:

```text
node node_modules/vitest/vitest.mjs run --configLoader runner --reporter=dot --maxWorkers=2
node node_modules/typescript/bin/tsc -b
node node_modules/vite/bin/vite.js build --configLoader runner
node scripts/package-ai-model.mjs
```

No new graph dependency, catalog schema or extraction pipeline was introduced. Accessibility, large-estate performance and export certification limits remain documented; passing tests do not establish exhaustive parity.
