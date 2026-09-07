# Phase 1 — tokens and shared primitives

> Current-state notice (2026-09-07): This document retains the earlier design/implementation record. Later owner-approved changes supersede its sidebar, Object inspector/Definition/Properties, graph, navigation and Alerts descriptions. Use [Current implemented UX](current-experience.md) and [ADR-0001](../adr/0001-sqlsync-investigation-workspaces-and-alerts.md) for the current contract. Original Figma frames and validation results remain historical evidence, not updated screenshots.

Gate 5 closed by D-016. This phase implements the approved foundations while retaining existing page compositions for their later migration phases.

## Changes

- Added tokens.css: light/dark semantic palette, spacing/radii, existing font stacks, control height and code colors. Light remains the default through the unchanged ThemeProvider. Existing shell variables alias the semantic tokens so dark mode also applies to the shell.
- Added a native, ref-forwarding Button visual primitive and reused it in ThemeToggle, CSV and XLSX actions. Existing event handlers, disabled conditions, labels, filenames and export contents remain owned by callers. XLSX adds aria-busy while exporting.
- Added shared focus-visible treatment, filter/search focus boundaries, 24px minimum clear/remove targets, tokenized chip treatment and readable placeholders.
- Moved code syntax colors to tokens, including the approved brighter comment color. Existing highlight.js classes/parser remain. GRANT/DENY, errors and diff additions/removals use semantic colors independently from the blue action accent.
- Added an accessible name to the existing DDL search input. No filtering, URL, data model, route or backend changes.

## Validation

Before editing: 213/214 tests passed with the default worker count; the real XLSX-writing test exceeded its existing five-second timeout. Rerunning with two workers passed all 214 tests. No timeout or assertion was weakened.

After changes: **222 tests passed in 29 files**, using:

```
node node_modules/vitest/vitest.mjs run --configLoader runner --reporter=dot --maxWorkers=2
```

Eight new cases protect light/invalid/saved-dark preferences, persistence and blocked storage; empty CSV disablement; all 501 supplied matching CSV rows; and XLSX lazy assembly, disabled busy state, error and retry. The theme test provides an in-memory Storage interface because this Node host's optional localStorage implementation shadows jsdom; real persistence was also checked in the browser.

TypeScript `tsc -b`, Vite production build and the existing AI packaging script passed. Packaging verified the existing all-MiniLM-L6-v2 model; this does not claim browser inference was tested. No dependencies were added.

Browser review at the existing 1280×720 viewport: captured Explorer before and after, checked light/dark, reloaded with dark saved, and verified keyboard focus. DOM inspection confirmed a 36px action control, 14px label and the approved dark background. The focused navigation link had a visible 2px focus outline. Restored light for the review tab. Screenshots are in the task's tool output; this is not a full screen-by-screen visual regression suite.

## Scope and next phase

This implements the foundation subset of the Figma contract, not the new Explorer arrangement or the complete AppShell. Existing table columns, routes and interactions remain present. Navigation/context layout, static snapshot presentation, narrow-screen shell behavior and page composition continue in Phase 2 and later phases. Existing type palette remains intact; full TypeBadge/graph contrast treatment is part of the later component migration.

Preview: http://127.0.0.1:5188/#/explorer. The initial 5173 port belonged to another local app, so SQLSync was started on 5188 without changing that app. No deploy or commit was performed. The pre-existing untracked NuGet.Config was not modified.
