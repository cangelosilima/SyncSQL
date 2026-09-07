# Follow-up changes — 2026-09-07

The final state is documented in [Current implemented UX](current-experience.md) and [ADR-0001](../adr/0001-sqlsync-investigation-workspaces-and-alerts.md). Earlier interim Properties placements and collapsible inspector designs are superseded.

- Catalog only on Explorer/Object, permanently rendered without panel toggle/Close; server-level linked servers skip schema nodes.
- Object explorer heading with adjacent Help; Definition stays above the lower workspace tabs. Relationships is between Access and Metrics and has its graph-evidence action at top right. Object inspector and duplicate Properties section removed.
- Persistent Lineage inspector; minimap removed. Direction-aware graph handles, consistent node sizing, increased spacing and arrowheads; single graph frame; collapsible legend above the graph.
- Alerts added between Lineage and AI with full anomaly/orphan counts, category/search URL filters, batches of 100, investigation links, detection explanations and Help. Existing Overview previews retained; extra Alerts shortcut removed.
- Corrected exact AI column-reference requests to use the existing column evidence workflow, with ambiguity/unsupported gates and local privacy preserved.
- Overview summary metrics are separate matching cards. Optimizer statistics spans the grid with contained scrolling. Column lineage top border restored, duplicate search focus outline removed, related-object badge spacing improved.
- Removed redundant shell server badges. Demo SIG linked server renamed REMOTE2 in the fixture and references, without executing SQL or renaming the actual remote host/database.

Latest full regression run: **253 tests passed across 38 files**, 38.32 seconds. See [validation](validation.md) and [test output](latest-tests.log). TypeScript, production build and packaged local model verification passed during the latest implementation changes. Browser observations remain scoped samples; no complete accessibility/performance/download certification or Figma synchronization is claimed.
