# Engineering handoff — Implemented

> Current-state notice (2026-09-07): This document retains the earlier design/implementation record. Later owner-approved changes supersede its sidebar, Object inspector/Definition/Properties, graph, navigation and Alerts descriptions. Use [Current implemented UX](current-experience.md) and [ADR-0001](../adr/0001-sqlsync-investigation-workspaces-and-alerts.md) for the current contract. Original Figma frames and validation results remain historical evidence, not updated screenshots.

Gate 5 was approved by D-016. All eight phases in this handoff are now implemented. See [implementation evidence and validation limits](implementation-complete.md). Earlier review wording below describes the design-stage contract, not an outstanding approval request.

The source application remains the functional contract. Approved design decisions are D-009 through D-015; the final refinements in this handoff still require Gate 5 closure. No broad React implementation is included in this design work.

Read [the 114-capability inventory](functional-parity-inventory.md), [selected interactions](selected-interactions.md), [component contracts](component-contracts.md), [Object workspace details](object-workspaces.md), [remaining page details](remaining-pages.md) and [validation baseline](validation.md) together. Existing source behavior wins when a static specimen omits a detail. Resolve material design/source conflicts explicitly; do not delete functionality to match a simplified drawing.

## Review entry points

Start at the [final Figma review index](https://www.figma.com/design/yAIbDoFO7bJd7Vm22nCc6p?node-id=132-363). Cross-page review controls use URL actions because Figma prototype NAVIGATE targets must be on the same page. The narrow inspector uses same-page transitions.

| Design artifact | Figma |
|---|---|
| Foundations | [75:10](https://www.figma.com/design/yAIbDoFO7bJd7Vm22nCc6p?node-id=75-10) |
| Explorer 390 / 1024 | [122:79](https://www.figma.com/design/yAIbDoFO7bJd7Vm22nCc6p?node-id=122-79) / [122:432](https://www.figma.com/design/yAIbDoFO7bJd7Vm22nCc6p?node-id=122-432) |
| Lineage 390 / 1024 | [122:369](https://www.figma.com/design/yAIbDoFO7bJd7Vm22nCc6p?node-id=122-369) / [122:515](https://www.figma.com/design/yAIbDoFO7bJd7Vm22nCc6p?node-id=122-515) |
| Narrow Lineage inspector | [122:419](https://www.figma.com/design/yAIbDoFO7bJd7Vm22nCc6p?node-id=122-419) |
| Object SQL / graph | [78:48](https://www.figma.com/design/yAIbDoFO7bJd7Vm22nCc6p?node-id=78-48) / [90:345](https://www.figma.com/design/yAIbDoFO7bJd7Vm22nCc6p?node-id=90-345) |
| Remaining pages | [Review links and data provenance](remaining-pages.md) |

## Incremental implementation sequence

| Phase | Concrete change | Acceptance before next phase |
|---|---|---|
| 1 | Tokens, theme defaults and shared visual primitives | Rerun existing suite before editing; stored theme respected; contrast/focus and no layout regressions; no routing changes |
| 2 | AppShell, context sidebar and common page structure | All five destinations, help, status and deep links preserved; keyboard and collapsed-context checks |
| 3 | Explorer token-bar composition | Existing filter grammar/DDL behavior; URL filters/q; seven sorts; 500/501 results; complete CSV; no removed table fields |
| 4 | Object workbench and inspector | All metadata/definitions/references, columns, grants, metrics, historical comparisons and exports accessible; local workspace state and column URL parity |
| 5 | Lineage Browse/Access composition | Focus stack, hop choices, filtering scope, exact/grantee behavior, cap breakdown, bundles, graph events and exports verified |
| 6 | Overview hierarchy | Source counts, heuristic thresholds, commit-touch units, reference reachability, ranking caps and all empty explanations verified |
| 7 | AI composition | All availability/loading/error/cancellation states; privacy; safe plan gates; filters/q transition and full matching count verified |
| 8 | History composition | Chronology, SHA/message/time/count, independent expansion and missing-object behavior verified |

Each phase is independently reviewable and retains a runnable application. Reuse existing helpers, memoization, graph layout, highlighting and lazy model/workbook loading. Do not add packages, extraction, schema fields or new analytics for visual convenience. No additional E2E framework has been approved; begin with the existing test tools.

## URL and data boundaries

Explorer uses `filters` and `q`. Browse Lineage uses `filter`, `focus`, `hops`, `q`; Access uses `tab` and `grantee` according to the existing replace/reset behavior. Object `column` remains a deep link. Do not homogenize these parameter names. Sorting, exact checkbox, revision selection and local navigation/workspace state are not silently made persistent or serialized.

Current catalog schema and optional/legacy arrays remain compatible. Preserve actual null values, exact timestamps, remote targets not extracted, system references and dynamic evidence. Access is extracted GRANT/DENY evidence. Historical DDL is separate from current metadata and operational metric snapshots. AI inference, heuristics, derived metrics and source facts remain distinguishable.

## Evidence required for implementation acceptance

The last executed frontend baseline is 214 passing tests across 27 files at e9c1683; this is not a fresh implementation test result. Rerun before the first code phase. Add behavior-level coverage for the gaps listed in validation.md, then update each parity row with actual evidence. Never mark Preserved=Verified based on a Figma screenshot.

Capture before, approved design and implementation at 1440×900, 1280×720, 1024×768 and 390×844, light/dark and 200% zoom. Exercise actual keyboard focus, screen-reader semantics, graph gestures, downloads, AI and performance against consistent fixtures. Preserve existing caps and debounces. Document justified visual deviations with the corresponding source capability and screenshot.

## Design review limits

The Figma file contains reusable foundation components, page compositions, responsive specimens, linked review paths and annotated edge states. It is not a complete interactive React simulation or a library where every complex composition has been republished as a variant set. Complex existing components are mapped in component-contracts.md. Synthetic AI, large-data and diff cases are explicitly labeled; they are not captured runtime results.

Gate 5 closure must explicitly accept the final review package and these limits. Implementation validation then remains mandatory; it is not waived by design approval. Optional enhancement proposals remain separately gated, and P2/P3 opportunities must not block the core refactor.
