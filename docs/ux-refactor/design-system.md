# Gate 5 — SQLSync design system

> Current-state notice (2026-09-07): This document retains the earlier design/implementation record. Later owner-approved changes supersede its sidebar, Object inspector/Definition/Properties, graph, navigation and Alerts descriptions. Use [Current implemented UX](current-experience.md) and [ADR-0001](../adr/0001-sqlsync-investigation-workspaces-and-alerts.md) for the current contract. Original Figma frames and validation results remain historical evidence, not updated screenshots.

Status: foundations and first core desktop compositions created; Gate 5 remains in progress. Gate 4 approved in D-012. No production CSS or application behavior has changed.

## Current Figma artifacts

The owner approved the initial foundations and desktop views in D-013. See [Object workspace and responsive extension](object-workspaces.md) for the subsequent six workspaces, source-backed metric series, historical/disclosure states, responsive views and twelve state specimens. Read object-workspaces-state.json alongside the earlier ledgers when resuming.

- [Foundations, light and dark](https://www.figma.com/design/yAIbDoFO7bJd7Vm22nCc6p?node-id=75-10): 70 variables (44 primitive, 26 semantic), six text styles, explicit scopes and CSS mappings. Semantic modes default to Light; Dark is available.
- [Explorer](https://www.figma.com/design/yAIbDoFO7bJd7Vm22nCc6p?node-id=78-40): all six AppDb fixture rows, eight columns, token/DDL separation and full CSV scope.
- [Object IDE workbench](https://www.figma.com/design/yAIbDoFO7bJd7Vm22nCc6p?node-id=78-48) and [dark comparison](https://www.figma.com/design/yAIbDoFO7bJd7Vm22nCc6p?node-id=81-16): Definition plus relationship inspector and full workspace destinations.
- [Lineage graph + inspector](https://www.figma.com/design/yAIbDoFO7bJd7Vm22nCc6p?node-id=78-56): focus-preserving scope, two-node fixture and recorded edge evidence.

Reusable families: Button (5 states), Input (4), FilterChip (3), StatusBadge (6), WorkspaceNav (4). Structure and screenshots were checked. Text properties were corrected to retain state-specific labels, and labels fill resized controls. Screen QA corrected a narrow Schema header. Tested text/status colors exceed 4.5:1 on intended surfaces; this is not complete accessibility certification.

Object workspace/responsive/state additions are approved D-014. [Remaining page compositions](remaining-pages.md) now add Overview, AI, History, Access and Explorer/Lineage 1280 examples. Remaining Gate 5 work includes other component families/semantic variants, full narrow-viewport and integrated state coverage, detailed handoff and final QA. SQL syntax-color refinement remains open; monochrome code is not a proposal to remove highlighting. Graph composition is not a gesture/performance test.

Read design-system-state.json for foundation IDs and design-system-progress.json for current component/screen IDs before resuming.

## Discovery and scope

Initial discovery found Source Sans 3, platform monospace, light/dark CSS colors, type colors and reusable filter, code, diff, metrics, related-object, graph, export and help components in code; no SQLSync library assets were returned by library search. The subsequent Figma library now has local variables, text styles and five component families. Six source-type evidence colors were added in the remaining-pages increment; they share values across themes and are used as shape accents with readable text labels.

Approved blue/light workbench direction supersedes red/pink shell colors. Source Sans 3 stays. Source Code Pro remains the approved monospace design reference; engineering must measure the existing platform stack before changing font delivery. Existing object-type colors remain evidence accents with readable labels. Preserve syntax semantics and validate theme contrast.

## Foundations

Latest handoff: [component contracts](component-contracts.md) and [final design review](final-design-review.md). Seven syntax variables now supplement the existing foundations; current SQL examples use them. The final review documents the five-family Figma library scope and reuse of existing complex React components.

Semantic colors: bg, surface, surface-alt, text, text-muted, border, accent, accent-contrast, selected, hover, focus, disabled, ok, warn, error, info, added, removed. Alias to primitives with Light first/default and Dark second. Existing names map to existing CSS custom properties; new names are proposed tokens.

Spacing: 4, 8, 12, 16, 24, 32. Radii: 4 and 8. Typography: Title 28/36 bold; Section 18/24 bold; Body 14/20 regular; Label 14/20 semibold; Caption 12/16 regular; Code 13/20 regular. No decorative shadows. Dense rows target 32px, controls at least 24px; balanced object details use 20px line height and 16–24px section spacing.

## Components and validation

Build Button, Input, FilterChip, StatusBadge and workspace navigation first. Compose shell, identity, tables, metadata, code/diff, inspector/drawer, graph, revisions, permissions, exports and help from reusable families. Preserve current model fields and interaction semantics. State variants cover normal, hover, focus, selected, disabled and loading as relevant; semantic warning/error/anomaly/orphan/GRANT/DENY/changed/added/removed cues include text and non-color distinction.

Validate 1440/1280 desktop, 1024, 390 and 200% zoom. Collapse catalog context first; constrained inspectors use explicit drawers. Tables and SQL retain horizontal scrolling and complete content. Handoff includes ownership, data sources, URL/state transitions, focus order and export contents. Gate 5 remains open until screen/component/state coverage and QA are complete.
