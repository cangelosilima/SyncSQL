# ADR-0001: SQLSync investigation workspaces and consolidated Alerts

- Status: Accepted and implemented locally
- Date: 2026-09-07
- Decision authority: product-owner instructions following the approved UX refactor and subsequent review in this task
- Scope: React presentation, navigation and derived investigation workflows

## Context

SQLSync already exposes deep database metadata, definitions, dependency evidence, permissions, history and metrics. The initial approved workbench design used collapsible inspectors, but owner review favored persistent information and fewer redundant controls. Graph connections were difficult to follow because horizontal placement used vertical attachment points and inconsistent node dimensions. Anomaly and orphan previews did not provide a consolidated way to investigate all findings.

The functional contract must remain intact: no simplification of lineage/access semantics, incomplete exports, fabricated metadata, hidden historical precision changes, or backend changes merely for visual convenience.

## Decision

1. Keep HashRouter and existing route/query semantics. Add `/alerts`, with primary order Overview, Explorer, Lineage, Alerts, AI, History. Alerts uses shareable `category` and `q` parameters.
2. Render Catalog only on Explorer and Object, permanently visible without open/close controls. Preserve branch expansion and bounded rendering. Do not create schema nodes for server-level linked servers.
3. Use a stable Object explorer heading with Help, preserve identity in context, and keep Definition permanently above lower workspace tabs. Order tabs Columns, Graph, Access, Relationships, Metrics, History, Diff. Remove the Object inspector and duplicated Properties section. Keep Relationships evidence and navigation to Graph; preserve revision/comparison behavior.
4. Keep a persistent Lineage inspector and stack it below the graph on narrow screens. Preserve focus, neighborhood/catalog distinction, access semantics and exports.
5. Retain React Flow and Dagre. Correct handle direction, align rendered and layout sizes, increase spacing, and use directional arrowheads. Remove the minimap and duplicate graph frame. Place a collapsible explanatory legend above the graph. No new graph dependency is justified by the current evidence.
6. Consolidate all existing metric-anomaly and orphaned-reference findings in Alerts. Use category/text filters, complete counts, bounded visible rows and source-object/lineage investigation links. Explain heuristics and catalog coverage. Do not introduce monitoring, alert lifecycle, new thresholds or collection.
7. Recognize supported exact column-reference requests in the local AI planner and hand off to the existing column workspace, rather than treating reference intent as literal DDL text. Reject ambiguous or unsupported requests conservatively.

## Alternatives considered

- **Keep collapsible inspectors everywhere:** rejected by owner review; it added controls and concealed expected content.
- **Keep Definition as a tab and Properties in the inspector/footer:** superseded by explicit owner instructions for persistent Definition and removal of duplicated Properties.
- **Replace React Flow with Cytoscape:** viable alternative toolkit, but requires rebuilding interaction/export integration. Deferred while fixing the concrete configuration issues first.
- **Adopt ELK immediately:** potentially useful for complex routing and ports; deferred pending evidence from larger graphs. No dependency installed.
- **Add new backend alert collection or scoring:** unnecessary for the requested consolidation. Existing catalog data and detector outputs suffice.

## Consequences

The UI exposes investigation context consistently and retains existing React components, data models and local-AI privacy. There are fewer dismissal controls and a new central Alerts destination. Persistent Definition increases vertical page length; persistent catalog/inspector regions stack on narrow screens. Graph layout uses estimated text height and can still produce crossings in complex/cyclic networks. Exports preserve graph content but do not reproduce every live curve or include the legend.

Alerts computes the full existing findings client-side and renders them in batches of 100. This adds client computation and retained finding objects, but no catalog storage or extraction cost. No new network disclosure is introduced. Absence of findings is explicitly not evidence of health. Dataset-scale performance is not certified by the small fixture review.

## Validation

Behavioral tests cover existing filtering, revisions, graph/access interactions, exports and local-AI flow; additions cover directional layout, bounded Alerts results and shareable filters, help registration and persistent workspace behavior. TypeScript, production builds and packaged model verification passed during implementation. Latest full-suite evidence is maintained in [UX validation](../ux-refactor/validation.md) and [latest-tests.log](../ux-refactor/latest-tests.log).

Browser spot checks used the existing catalog. Accessibility and large-estate certification limits are recorded in [current experience](../ux-refactor/current-experience.md). This ADR documents accepted local changes; it is not evidence of deployment, Figma synchronization or exhaustive behavioral parity.

## Follow-up

Measure crowded graphs before choosing a new layout engine. Review at representative large-catalog sizes and complete broader accessibility/export validation. Any new enrichment or backend contract requires a separately scoped decision.
