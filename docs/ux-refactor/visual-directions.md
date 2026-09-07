# Gate 3 — Visual direction discovery

> Current-state notice (2026-09-07): This document retains the earlier design/implementation record. Later owner-approved changes supersede its sidebar, Object inspector/Definition/Properties, graph, navigation and Alerts descriptions. Use [Current implemented UX](current-experience.md) and [ADR-0001](../adr/0001-sqlsync-investigation-workspaces-and-alerts.md) for the current contract. Original Figma frames and validation results remain historical evidence, not updated screenshots.

Status: Gate 3 approved. Owner selected **A — Developer Tool, light by default, dense results and balanced object details**. The original dark A study is retained as decision history; light is the approved default and dark remains available. Gate 2 separately selected C (catalog sidebar + workspace).

## Visual comparisons

All live on `00 — UX Direction` in the [SQLSync discovery file](https://www.figma.com/design/yAIbDoFO7bJd7Vm22nCc6p?node-id=2-2).

| Direction | Figma | Concrete properties | Investigation benefit | Tradeoff |
|---|---|---|---|---|
| A — Developer Tool | [View A](https://www.figma.com/design/yAIbDoFO7bJd7Vm22nCc6p?node-id=18-2) | Dark slate surfaces, blue emphasis, compact spacing, restrained object title, SQL first | Fast repeated scanning of SQL and technical metadata | Dark presentation is only a sample; both themes remain required |
| B — Engineering Intelligence | [View B](https://www.figma.com/design/yAIbDoFO7bJd7Vm22nCc6p?node-id=18-3) | Light cool surfaces, violet emphasis, larger measurements, snapshot comparison first | Investigate measured change with caveats close to the evidence | Must not imply live telemetry, alerting, or authoritative risk |
| C — Modern Data Catalog | [View C](https://www.figma.com/design/yAIbDoFO7bJd7Vm22nCc6p?node-id=18-4) | Light neutral/green surfaces, stronger identity and description, relationships first, more spacing | Understand an object's role and location before going deeper | SQL and dense fields must stay close; extra space increases scrolling |

[Density comparison](https://www.figma.com/design/yAIbDoFO7bJd7Vm22nCc6p?node-id=21-10) shows the same three SQLSync objects as dense rows, balanced entries and spacious cards. It is deliberately independent of palette and identity style. Three sample records are not a new result cap. The existing eight Explorer columns and all export fields remain required regardless of selected density.

## Shared content and boundaries

- All directions retain the selected collapsible catalog context sidebar plus five primary destinations. AI's unavailable deployment state remains visible; this does not approve its relocation.
- Source: `site/public/data/catalog.json`. Main example is dbo.Orders in SQLPROD01 / AppDb / dbo, description Customer orders; Id, UserId and Total columns; current DDL; one direct dependency and two direct consumers; app_reader SELECT and app_writer INSERT grants; last-change date 20 Aug 2026.
- Measurements are bundled sample snapshots: row counts 55,800 → 106,020 and IX_Orders_UserId fragmentation 8% → 68%. The 90% change is existing detector output. Findings are heuristic interpretations of measurements. The B bar lengths use a common zero baseline and scale proportional to the displayed counts; they are not an invented trend series.
- Source facts and derived relationships/changes remain distinguishable from heuristic classification. No AI-inferred field or source enrichment is fabricated.
- SQL, column, access, relationship and metric evidence is retained across directions. History/export and remaining sections are represented by navigation affordances, not deleted. These are direction studies, not complete state or behavioral specifications.
- Section navigation and side-by-side content are illustrative compositions, not a selection of Object tabs or IDE layout. Gate 4 must still compare long document, workbench tabs and IDE inspector using the same object. Lineage graph-first/list+graph/graph+inspector and Explorer filter arrangements also remain open.
- No production component library, token set, backend/schema/URL change, command palette, tree interaction policy or broad UI implementation is approved by these boards.

## Recommendation and review

For the confirmed developer-first priority, start the discussion from A's technical emphasis and dense/balanced scanning, while evaluating whether C's clearer identity or B's evidence emphasis better matches actual investigations. This is a recommendation to compare, not a selected hybrid. A palette choice must not become an automatic default theme decision.

Gate 3 response recorded: A, light by default, dense results, balanced object details. The recommendation above is retained as discovery history. Continue with [Gate 4 model comparisons](core-workflows.md); the approved catalog-sidebar topology and these visual preferences do not need reconfirmation.

## Design/validation record

- The earlier asset discovery remains applicable: no SQLSync Code Connect mappings or existing component library; only unrelated community kits are available. Their visual languages are not imported.
- Product font Source Sans 3 retained; Source Code Pro is the provisional monospace study font in place of platform-dependent monospace. Families/styles verified against available Figma fonts before mutation.
- Draft palettes/layout values are deliberately exploratory, not published foundations. The user requires the design system after direction approval.
- All containers use editable auto-layout. The three directions and separate density board were rendered and visually inspected. Validation covers these study frames only, not responsive behavior, WCAG compliance or runtime parity.
- No application or test files changed. The earlier 214-test baseline remains the code baseline; documentation/Figma-only work did not warrant rerunning application tests.
