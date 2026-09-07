# Gate 2 — Information architecture alternatives

> Current-state notice (2026-09-07): This document retains the earlier design/implementation record. Later owner-approved changes supersede its sidebar, Object inspector/Definition/Properties, graph, navigation and Alerts descriptions. Use [Current implemented UX](current-experience.md) and [ADR-0001](../adr/0001-sqlsync-investigation-workspaces-and-alerts.md) for the current contract. Original Figma frames and validation results remain historical evidence, not updated screenshots.

Status: Gate 2 approved; owner selected **C — Catalog sidebar + workspace** on 2026-09-06. The earlier recommendation B is retained below as decision history and was not selected. Detailed tree behavior and enhancements remain unapproved until specified.

Editable Figma comparisons: [A — Top navigation](https://www.figma.com/design/yAIbDoFO7bJd7Vm22nCc6p?node-id=7-3), [B — Persistent left navigation](https://www.figma.com/design/yAIbDoFO7bJd7Vm22nCc6p?node-id=7-4), [C — Catalog context sidebar](https://www.figma.com/design/yAIbDoFO7bJd7Vm22nCc6p?node-id=7-5), [D — Command-driven navigation](https://www.figma.com/design/yAIbDoFO7bJd7Vm22nCc6p?node-id=7-6).

These are low-fidelity topology comparisons, not complete screen specifications. The same Object content and illustrative originating filter context are held constant; their arrangement is not an approved Object/Explorer redesign. A return-context strip, selectable estate tree and command actions are marked proposals. AI runtime-state variants remain part of later state specifications.

### Figma discovery record

- Source font: `Source Sans 3` from styles.css; Figma provides it. `Source Code Pro` is an explicitly provisional monospace substitute for the application's platform monospace stack.
- Code Connect: no *.figma.* files found in site. Existing Figma content: audit text only; no SQLSync components, local variables or text styles.
- Library inspection found community kits but no SQLSync library. No community visual language is adopted. User requires discovery before design-system consolidation; these disposable wireframe compositions intentionally do not establish reusable production components or tokens.
- No raster images are required. All diagrams are editable text/auto-layout frames; no page capture is being recreated.
- Screenshot review corrected a fixed-height horizontal container that clipped the workspace. All four final comparisons are reviewed for readable content. This is Figma layout validation, not runtime or accessibility certification.

## Shared task and unchanged functional contract

All four alternatives use the same source-backed example: find **dbo.Orders**, the **Customer orders** table in **SQLPROD01 → AppDb → dbo**, inspect its definition and known consumers, then open focused Lineage. Data comes from [the existing catalog](../../site/public/data/catalog.json). No fabricated scores, ownership or live health are needed to compare navigation.

Each alternative retains **Overview, Explorer, AI, Lineage and History** as explicit destinations and retains the Object route as an investigation destination. AI remains visibly unavailable when the optional local runtime cannot be used. Access remains inside Lineage. History remains the bounded mined commit timeline. Alternate naming, removal, merging or relocation is not implied by selecting top or side navigation.

The owner's confirmed priorities are database/application developers, large estates, and preservation of investigation context. The shell should make the current location clear while leaving space for technical work. A shell alone cannot restore lost filter/revision/graph state: new return-context persistence remains a separately specified and approved enhancement.

## Four alternatives

| Alternative | Concrete composition for dbo.Orders | Developer/context benefit | Large-estate and narrow-viewport implications | Tradeoff and approval boundary |
|---|---|---|---|---|
| **A — Retained top navigation** | Top row lists Overview / Explorer / AI / Lineage / History. Object identity and SQLPROD01 → AppDb → dbo appear below, above the current workspace. | Familiar destination model; largest horizontal working area for SQL and tables. Current investigation is identified locally. | No estate tree to render. At constrained width, compact shell into a labeled navigation disclosure containing all five destinations and leave object context visible. Server-status overflow needs a readable disclosure, not missing server names. | Smallest IA migration. Global status, destinations and long object context can compete vertically. Does not solve return-state loss. |
| **B — Persistent left navigation — recommended** | A restrained left rail lists all five destinations in text. The center header identifies dbo.Orders and its server/database/schema; snapshot context remains clearly labeled. | Stable landmarks across Explorer, Object and Lineage support frequent investigation switches. Separates global destinations from object-level work without inventing a new workflow. | Fixed destination count scales independently of catalog size. At constrained width, rail becomes a labeled menu/drawer with all five destinations; restore focus to trigger when closed. Central tables/code retain horizontal scrolling where necessary. | Uses some desktop width. Recommend a compact rail, not a catalog tree; final dimensions and component behavior wait for wireframes/design system. No AI merge or Access extraction. |
| **C — Collapsible catalog context sidebar + workspace** | All five destinations remain visible in the global shell. A collapsible secondary panel shows the current SQLPROD01 → AppDb → dbo context next to dbo.Orders workspace. | Makes location within the estate prominent; potentially useful when repeatedly moving among objects in one database. | A full tens-of-thousands-node tree must not be assumed or mounted wholesale. Initial comparison uses current-object context; any browsable hierarchy/search/lazy-loading behavior needs its own approved contract. On narrow screens context becomes an on-demand panel and the workspace takes full width. | Two navigation layers can be confused. A new selectable catalog hierarchy is a feature proposal, not existing filtering automatically represented as a tree. Panel openness persistence is not approved. |
| **D — Command/search-driven navigation with visible fallback** | A prominent “Find an object” entry leads to existing Explorer search; all five destinations remain explicit in a compact navigation area. dbo.Orders keeps a visible identity/context header. A command-palette concept is labeled proposed. | Could reduce pointer travel for repeat users while preserving discoverability through visible destinations. Existing Explorer can remain the search authority. | Existing capped/sorted Explorer handles results; do not infer a second uncapped search index. At constrained width the search entry and labeled destination menu remain reachable. No shortcut-only route. | Existing navigation can be emphasized now in designs; actual global search, shortcuts or command palette introduce interaction/state contracts and require enhancement approval before implementation. Highest discovery/keyboard-specification burden. |

### Earlier recommendation B (not selected)

B gives developers stable global landmarks while separating object identity from the navigation list. Its five entries do not grow with database-estate size, and it works with any later Object document/tab/inspector decision. This is a narrower commitment than introducing a catalog hierarchy or a command palette. It still needs comparison at code/table-heavy desktop widths; if lost horizontal space is unacceptable, A is the fallback candidate. The owner must select or reject the recommendation after seeing the alternatives.

Navigation topology and visual tone are separate decisions. Choosing B does not choose a dense or spacious interface, dark or light palette, tabs, an IDE split view, or an observability dashboard.

## Route, state and parity mapping

| Existing capability IDs | Preserved across A–D | Contract requiring care |
|---|---|---|
| SH-01–SH-07 | All routes, active destinations, availability states, version, snapshot/server context | HashRouter and slash-containing `/object/*` identity stay intact. Server indicators cannot become invented live health. |
| OV-01–OV-12 | Snapshot intelligence, anomalies, orphaned references, activity, change and relationship rankings | All Overview information remains available; navigation selection does not approve reorganizing or recategorizing its metrics. |
| EX-01–EX-08; FI-01–FI-13 | Explorer metadata tokens, separate DDL search, count, sorting, 500 visible-row cap and complete matching CSV | `/explorer?filters=…&q=…`; updates replace history and retain unrelated params. Sort is local today. Sidebar visuals must not remove operators or convert AND to another logic. |
| OB-01–OB-23; RE-01–RE-05 | Object identity, all sections, relationships, metrics, grants, historical definitions/diff and downloads | `/object/<id>?column=…`; column selection is shareable, revisions are local. Bare Explorer breadcrumb currently loses filter context. A proposed return-context fix is not approved by shell selection. |
| LI-01–LI-15; GR-01–GR-08 | Browse/Access, focused neighborhood distinction, graph controls, drill history, grantee evidence and exports | Browse uses singular `filter`, `focus`, `hops`, `q`; Access uses `tab=access` and `grantee`. Exact matching and full drill stack are not serialized. Preserve this distinction until an explicit URL change is approved. |
| AI-01–AI-11 | Primary AI destination, unavailable route, local loading/retry/cancel/preview and Explorer handoff | AI handoff uses ordinary Explorer `filters`/`q`. No inference/search executed merely by focusing global navigation. |
| HI-01–HI-04 | History destination, bounded chronology, independent commit expansion and object links | No new Changes/Impact area or additional history mining is approved. |
| SH-08–SH-11; DL-01–DL-04 | Contextual help, themes, type semantics and current download contents | Drawers/menu proposals need keyboard/focus specification; theme and export contracts do not depend on which navigation alternative is selected. |

## Open decisions and next gate

- **Gate 2 selection:** Owner selected C. The collapsible catalog context sidebar and workspace are the approved topology; earlier alternatives remain as decision history.
- **Separate product decisions:** moving AI into Explorer, promoting Access outside Lineage, reframing History as Changes/Impact, global search/command palette, browsable estate hierarchy and investigation-state persistence remain unapproved. These must not slip into implementation through an IA diagram.
- **Gate 3:** compare Developer Tool, Observability/Engineering Intelligence and Modern Data Catalog visual directions independently of topology.
- **Gate 4:** specify Explorer arrangement, Object document/tabs/inspector and Lineage graph/list/inspector, with density, keyboard behavior, responsive transitions and exact state changes. Desktop investigation remains primary; narrow layouts must preserve access to every field/action.

Acceptance of Gate 2 establishes navigation structure only. Engineering should map approved placements back to the 114-row inventory, test route/availability/URL compatibility, and retain all unresolved behavior gaps in the validation plan. Broad UI implementation still waits for Gate 5.
