# Design Decision Log

| ID | Decision | Status | Basis / consequence |
|---|---|---|---|
| D-001 | Prioritize database and application developer investigations | Confirmed, 2026-09-06 | Owner selected Developer investigation; other personas remain supported |
| D-002 | Target large estates: tens of thousands of objects and hundreds/thousands of relationships around hubs | Confirmed, 2026-09-06 | Owner selected Large estate; exact production counts/hardware are not supplied |
| D-003 | Lead with preservation of investigation context | Confirmed, 2026-09-06 | Owner selected Losing investigation context |
| D-004 | Preserve all existing capability, data, URL, export and explanatory semantics | Required | Original product brief; an interaction may change only through explicit review |
| D-005 | Use separate UX and engineering ownership | Required | UX specifies experience; engineering validates feasibility/parity and implements approved contracts |
| D-006 | Use existing catalog information before proposing new collection | Required | Enrichment provenance must distinguish source fact, derived metric, heuristic and AI inference |
| D-007 | Do not treat “Continue” as approval of gates not yet presented for acceptance | Applied | Complete review artifacts and request Gate 1 explicitly |
| D-008 | Accept the current-experience audit and Functional Parity Inventory as the discovery baseline | Approved, 2026-09-06 | Owner explicitly approved Gate 1; recorded verification gaps remain open. Authorizes progression to Gate 2 comparison, not an IA choice, enhancement or broad UI implementation |
| D-009 | Select Gate 2 option C: collapsible catalog context sidebar + central workspace | Approved, 2026-09-06 | Owner selected “option C.” Preserve five global destinations; detailed tree selection, filtering, state lifetime and keyboard behavior await Gate 4. No global command palette, AI relocation or catalog-schema change is approved |
| D-010 | Select Developer Tool visual direction A, light by default, dense results and balanced object details | Approved, 2026-09-06 | Owner explicitly selected these properties. Dark theme remains available. This approves visual direction/density, not Object tabs, tree filter semantics or Lineage layout |

## Gate ledger

D-011 — Approved, 2026-09-06: owner selected **Explorer Token bar, Object IDE inspector, Lineage Graph + inspector**. This supersedes the provisional Object-tabs recommendation. Authorizes detailing E1/O-C/L3; no new URL schema, persistence, data collection or unrelated enhancement is implied. Inspector presentation is selected; its detailed controls remain part of Gate 4 review.

| Gate | Deliverable | Status |
|---|---|---|
| 1 | Current audit + functional parity inventory | Explicitly approved, 2026-09-06; verification gaps retained |
| 2 | Information architecture alternatives | Approved: option C, catalog sidebar + workspace |
| 3 | Three concrete visual directions | Approved A: Developer Tool, default light; dense results and balanced object details |
| 4 | Explorer, Object and Lineage workflow wireframes | Approved, 2026-09-06: selected interaction contract and its explicitly stated prototype/validation limits |
| 5 | High fidelity + design system | Closed by D-016; incremental implementation authorized with the explicit design representation and runtime-validation limits |
| 6 | Optional functional/data enhancements | Proposed individually; none approved |

## Decisions deliberately open

D-016 — Approved, 2026-09-06: owner approved Gate 5 closure and the final design/handoff package, including its representation limits. Authorizes incremental implementation starting with tokens and shared primitives. Runtime parity, accessibility and performance evidence remain required for each phase; optional enhancements and backend/catalog changes remain separately gated.

D-015 — Approved, 2026-09-06: owner approved the remaining-page increment: Overview, AI unavailable/simulated preview, History disclosures, Access, eighteen state specimens and compact desktop layouts described in remaining-pages.md. Retain their explicit simulation and validation boundaries. Proceed to the final design handoff; this is not approval of data enrichment or a claim that runtime parity has been tested.

D-014 — Approved, 2026-09-06: owner approved the presented Object workspace extension, responsive examples and state specimens. Continue the same visual treatment for remaining pages. Recorded prototype/validation limits remain; data enrichment and unpresented behavior are not implicitly approved.

D-013 — Approved, 2026-09-06: owner approved the presented first Gate 5 desktop compositions and foundations (Explorer 78:40, Object 78:48, Lineage 78:56, foundations 75:10). Carry their visual treatment forward. The explicitly listed unfinished workspaces, responsive designs, state coverage and handoff remain to be completed; this approval does not fabricate completion of those artifacts.

D-012 — Approved, 2026-09-06: owner explicitly approved the presented Gate 4 interaction contract. Proceed with high-fidelity design and design-system work. Validation limits remain recorded; approval is not a claim of passing keyboard, responsive, graph or export tests. Tree expand/navigation, local workspace state and inspector dismissal follow the approved contract. No broader enrichment, global command palette or backend change is authorized.

Catalog context sidebar + workspace, Developer Tool direction, default light, dense results and balanced object details are approved. Explorer token bar, Object IDE inspector and Lineage graph + inspector follow the approved Gate 4 contract. AI relocation and History expansion are unapproved; their existing primary destinations remain. D-013 approves the first presented Gate 5 designs without claiming completion of the remaining coverage.

## Future decision entry

## Post-implementation owner decisions — 2026-09-07

These accepted decisions supersede the earlier interaction choices where they conflict; they do not reopen the original gates. See [current contract](current-experience.md) and [ADR-0001](../adr/0001-sqlsync-investigation-workspaces-and-alerts.md).

| ID | Accepted decision | Scope and rationale |
|---|---|---|
| D-017 | Catalog only on Explorer/Object, always visible, no toggle/Close | Preserve hierarchy navigation and limits; remove artificial schema branch for server linked servers |
| D-018 | Persistent Definition above Object tabs; Relationships between Access and Metrics; no Object inspector | Preserve investigation content and revision state; All relationship evidence opens Graph from the panel's top right |
| D-019 | Remove duplicate Properties section; use Object explorer heading and adjacent Help | Supersedes interim moves of Properties into Relationships, then a horizontal footer; object identity remains in breadcrumb/metadata |
| D-020 | Persistent Lineage inspector without dismissal; remove graph minimap | Simplify controls; narrow layouts stack inspector below graph |
| D-021 | Retain React Flow/Dagre and correct horizontal handles/dimensions/spacing | Owner accepted the proposed configuration repair; ELK/Cytoscape remain deferred. Add arrows and collapsible legend above the graph, remove duplicate frame |
| D-022 | Add Alerts between Lineage and AI | Explicit new-feature authorization: consolidate existing anomaly/orphan signals, shareable category/search and investigation links; no backend/monitoring additions |
| D-023 | Correct AI column-reference intent using existing evidence | Resolve exact known object/column; block ambiguity/unsupported clauses; preserve literal DDL search and local execution |
| D-024 | Overview metrics as separate matching cards; remove Investigate all alerts link | Alerts stays in primary navigation. Existing Overview preview information remains |
| D-025 | UI/fixture corrections | Metrics table containment, column panel top border, single search focus outline, badge spacing, remove server badges, rename demo SIG linked server to REMOTE2 |

The earlier Gate 6 “none approved” statement describes the original proposal review. D-022 now authorizes the specific Alerts scope; it does not approve unrelated enrichment or a quantified metric-coverage feature.

For each decision record: ID/date, problem and capability IDs, alternatives shown (Figma node links), owner's response, accepted interaction/URL differences, rejected options and rationale, data proposal IDs, validation requirements, and gate approval. Never overwrite history when a later decision supersedes one.
