# Gate 5 — final design review

> Current-state notice (2026-09-07): This document retains the earlier design/implementation record. Later owner-approved changes supersede its sidebar, Object inspector/Definition/Properties, graph, navigation and Alerts descriptions. Use [Current implemented UX](current-experience.md) and [ADR-0001](../adr/0001-sqlsync-investigation-workspaces-and-alerts.md) for the current contract. Original Figma frames and validation results remain historical evidence, not updated screenshots.

Approved by the owner as D-016. [Phase 1 implementation](implementation-phase-1.md) now begins the authorized incremental migration; the review request below is retained as decision history.

The selected experience is ready for final design approval with the representation limits below. D-015 already approves the preceding page increment; this request concerns the final refinements and engineering handoff, not another direction choice.

[Open the Figma review index](https://www.figma.com/design/yAIbDoFO7bJd7Vm22nCc6p?node-id=132-363).

## Final additions

- Explorer and Lineage at 390 and 1024 CSS pixels; full table fields, explicit scroll, retained filter scope and inspector access. The narrow two-node graph reflows vertically to keep its focus visible.
- Syntax tokens applied to current/historical Object SQL and responsive examples; existing highlighter remains the engineering implementation. Text contrast on #282a36 ranges from 5.90:1 to 13.36:1. Proposed comment color is #a1acc8 (6.27:1).
- Object graph minimap populated with the same four nodes/five edges; type-colored accents applied to desktop Object/Browse/Access nodes. Responsive clones are layout references; implementation uses the shared GraphNode treatment rather than preserving pre-refinement clone colors.
- Full-page Explorer no-match (CSV disabled), AI loading/cancel and 301-object Lineage breakdown examples. A separate synthetic diff specimen demonstrates added/removed labels without altering real identical revisions.
- [Component contracts](component-contracts.md) covering all requested component categories, semantic/interaction states and existing-component reuse.
- [Engineering handoff](engineering-handoff.md) covering eight migration phases, URL/data contracts, regression coverage and visual/runtime acceptance.

## Representation and validation limits to accept

Five reusable Figma component families are published locally. Complex code, diff, graph, metrics, export and help compositions are specified and mapped to existing React components; they are not all separate Figma variant libraries. Edge states combine full-page examples and annotated specimens. The prototype contains review links and selected state transitions; it is not a complete executable simulation of the application.

These limits concern the design deliverable, not product scope: all 114 capabilities remain required. No runtime parity, actual 200% browser zoom, screen-reader compliance, model execution, graph performance or download correctness is claimed by the design review. Those remain mandatory engineering acceptance checks before each migration phase is considered complete.

Figma widths, screenshots, syntax contrasts and selected reaction targets were checked. The existing frontend baseline remains 214 passing tests in 27 files; application code was not modified during design work. Final implementation must rerun the baseline and produce BEFORE / APPROVED FIGMA / IMPLEMENTATION evidence.

## Approval requested

Accept this final design/handoff package and its stated representation limits as Gate 5 closure, authorizing incremental implementation beginning with tokens and shared primitives. No optional enhancement, new data collection, backend/catalog change, new dependency or AI relocation is included.
