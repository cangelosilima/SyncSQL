# Latest approval scope — 2026-09-07

The owner explicitly requested and authorized **Alerts** (D-022). It consolidates existing anomaly and orphan data without changing collection or detection. This implements the full-findings aspect of PEP-03 in Alerts; Overview still retains its preview limits. PEP-02's quantified coverage remains unimplemented; explanatory availability caveats alone do not fulfill it. Earlier proposal statuses below describe their original review stage.

| Enhancement | Priority | Persona / problem | UX and functionality | Data / source | Backend / collection | Complexity | Recommendation |
|---|---|---|---|---|---|---|---|
| Alerts consolidation | P1 | Developers/support need to investigate all findings beyond Overview previews | New route, counts, category/text URL filters, batches of 100 and object/lineage links | Existing metric detector outputs (heuristic) and catalog orphan findings; no new fields | None | Low–medium | Authorized and implemented |

Performance: computes all existing findings in the browser; bounds DOM rendering to requested batches. Storage: transient client arrays/query state only, no new catalog data. Security/privacy: no new external transmission; same catalog exposure. Compatibility: additive `/alerts` with `category`/`q`, no changes to existing URLs. Tests: complete-count/100-row boundary, Show more, category/search, deep links, help and navigation targets. No alert lifecycle, background notifications or new severity/risk score is implied.

## Original enhancement proposal register

# Product Enhancement Proposals

Status: all proposals unapproved. Nothing here is included automatically in the UI refactor. Gate 1 acceptance does not accept these proposals.

No new data collection is required for the core parity refactor. Row counts, sizes, index usage, optimizer-statistics snapshots, descriptions and co-change pairs already exist in the current model; proposing them as wholly new collection would be incorrect.

| Enhancement | Priority | UX benefit | Data required / source | Backend impact | Complexity | Recommendation |
|---|---|---|---|---|---|---|
| PEP-01 Investigation return context | P1 | Return to the exact filtered exploration after inspecting related objects | Existing URL filters/q, local sort and navigation state | None | Medium | Prototype and seek approval at core workflow gate |
| PEP-02 Anomaly evidence coverage | P1 | Distinguish insufficient comparable measurements from no threshold crossings | Existing metrics snapshots and nullable metric fields | None | Low–medium | Propose alongside Overview states; not a core blocker |
| PEP-03 Anomaly total vs top findings | P2 | Know whether the ten displayed findings are the complete result | Existing detector's full findings before truncation | None | Low | Defer until Overview migration |
| PEP-04 Relationship counts in Explorer | P1 | Judge connectivity while locating an object | Existing incoming/outgoing index and CSV fields | None | Low | Compare an optional information treatment before approval |
| PEP-05 Documentation coverage | P2 | Identify objects/columns with missing extracted descriptions | Existing description fields | None | Low | Defer; coverage is not documentation quality |
| PEP-06 Ownership / sensitivity / last access | P3 | Potential future routing of investigation to accountable teams or usage evidence | Not established in current catalog contract | Potential extraction/schema/pipeline changes | High / unresolved | Research only after a specific user problem is prioritized |
| PEP-07 Selectable catalog hierarchy | P2 | Move among nearby objects without re-entering their context | Existing server/database/schema/object identity | None | Medium | Gate 2 option C; prototype only until approved |
| PEP-08 Global command navigation | P2 | Reach an object or investigation destination with fewer pointer actions | Existing object identity and route map | None | Medium | Gate 2 option D; prototype only until approved |
| PEP-09 Browse neighborhood list | P2 | Scan selected objects alongside the graph | Existing bounded neighborhood selection | None | Medium | Optional Gate 4 L2 interaction |
| PEP-10 Focus inspector | P2 | Keep object and edge evidence beside the graph | Existing focused object, relationships and column references | None | Medium | Optional Gate 4 L3 interaction |

P0 applies to correctness of an approved UX. No new P0 collection requirement has been established. Clarifying static-snapshot, bounded-history and existing best-effort reference explanations is a correctness task within the refactor, not an invented metric. If an approved design requires coverage counts, PEP-02 can be reconsidered as P0 for that design; current discovery alone does not justify that escalation.

## PEP-01 — Investigation return context

- Problem/persona: developers lose the Explorer's filters when using the Object breadcrumb, and need to resume their original investigation.
- Proposed UX/functionality: an explicit return-to-results action restores originating filters/content search and, if approved, sort and scroll. It remains distinct from global Explorer navigation. Direct object links continue to work without an origin.
- Data/source/provenance: existing URL tokens and ephemeral browser UI state; navigation metadata, not catalog evidence.
- Collection/backend/storage: no extraction, catalog or backend change. Prefer per-navigation session memory/history state; persistence across browser sessions is not included. Exact state lifetime and behavior on browser back/forward must be approved with wireframes.
- Complexity/performance: medium; avoid copying result arrays or catalogs into route state. Retain only identifiers/query and bounded UI metadata.
- Security/privacy: no upload; query strings may already contain catalog-sensitive search text. Do not introduce prompt persistence or cross-user sharing.
- Compatibility: existing deep links and parameter names remain valid; cross-route history behavior must not be changed silently. Handle originless links and changed catalog contents.
- Testing: filtered/sorted Explorer → object → related object → return; direct object entry; browser back/forward/reload; invalid origin; empty or changed results; scroll restoration under large fixtures.

## PEP-02 — Anomaly evidence coverage

- Problem/persona: support engineers and developers cannot tell no detected anomaly from no comparable metric evidence.
- Proposed UX/functionality: alongside findings, explain whether comparisons were possible and the relevant missing evidence; show comparison coverage only if approved. Expose the existing rules rather than an unexplained risk badge.
- Data/source/provenance: deterministically derived coverage from `node.metrics`; anomaly classification remains a heuristic. Comparability must be per signal, not merely “two snapshots exist”: row counts need usable current/previous values, fragmentation has its existing newly observed-index behavior.
- Existing rules: row-count movement at least 50% and 500 rows with previous count above zero; fragmentation at least 30% and a jump of at least 20 percentage points, with current behavior for a previously missing index. Retain these rules unless separately approved.
- Collection/backend/storage: none; no stored derived field. Read existing snapshots only.
- Complexity/performance: low–medium; compute coverage and findings together in a memoized pass, respecting current retention. Avoid repeated metric scans per UI row.
- Security/privacy: existing catalog data only, no telemetry or external inference.
- Compatibility: add internal derived output without requiring a new catalog version; null/missing fields remain unknown. Do not call coverage an estate-wide monitoring guarantee.
- Testing: no snapshots, one snapshot, two null snapshots, comparable row counts, prior zero, missing/new index, thresholds just below/at/above limits, engine-specific fields, no findings with evidence.

## PEP-03 — Full anomaly count

- Problem/persona: developers/support may read the displayed maximum of ten as the total number detected.
- Proposed UX/functionality: “Showing 10 of N findings,” preserving the same top-ten ordering and object links. Count findings, not distinct affected objects, unless both are explicitly requested.
- Data/source/provenance: deterministic aggregate of existing heuristic results; the count is derived, the findings remain heuristic.
- Collection/backend/storage: none; calculate before display truncation, no serialized schema changes.
- Complexity/performance: low; detector already scans the nodes, but retain only necessary ranking/count state if scale warrants it.
- Security/privacy: no additional exposure beyond current catalog.
- Compatibility: preserve existing default helper behavior for callers or update through an internal wrapper; no externally required catalog field.
- Testing: 0/1/10/11 findings, multiple findings on one object, stable ranking and count independent of top-ten display.

## PEP-04 — Relationship counts in Explorer

- Problem/persona: developers must open an object or export it to see counts that help choose the right investigation target.
- Proposed UX/functionality: show existing direct Depends on/Used by counts in a reviewed table or inspector treatment. Existing columns remain accessible. New sorting/filtering of these fields is not implied by this proposal.
- Data/source/provenance: derived from current indexed relationships, already exported by `objectColumns`. Do not substitute unrestricted transitive counts.
- Collection/backend/storage: none; no new schema or persisted aggregation.
- Complexity/performance: low for map lookups; avoid running BFS for every visible row or every sort comparison.
- Security/privacy: existing catalog only; no new collection.
- Compatibility: export fields and counts remain identical; absence of extracted edges does not prove absence of consumers.
- Testing: same counts as object/export, duplicate edges normalization, zero edges, cyclic relationships, large results, null/missing linked targets.

## PEP-05 — Description coverage

- Problem/persona: newcomers and architects need to understand which extracted metadata has descriptions.
- Proposed UX/functionality: a bounded “descriptions present” summary with a clear denominator; navigation to missing descriptions would be an additional explicit filtering proposal.
- Data/source/provenance: derived from current object/column descriptions; not an AI quality judgment or inferred ownership.
- Collection/backend/storage: none; count at catalog indexing or memoized page scope, not persistently.
- Complexity/performance: low, one linear scan; no repeated per-render traversal.
- Security/privacy: no new exposure; no text sent to a model.
- Compatibility: describe extraction/engine limitations and distinguish null/empty values. Do not compare engines as documentation quality without establishing collection equivalence.
- Testing: null/empty/whitespace descriptions, zero denominator, mixed object types/engines, object versus column denominators.

## PEP-06 — Future collected information (not implementation-ready)

This entry is a research backlog item, not a request to implement all three examples.

- Problem/persona: an owner must first identify a concrete task requiring ownership, sensitivity or usage information and whether it is essential or optional.
- UX/functionality: location and presentation depend on that task; no field is to be fabricated in a prototype as an existing fact.
- Data/provenance: use a named system of record for source facts. Deterministic mappings are derived; scoring is heuristic; model output is AI inference. Never treat missing last-access evidence as proof that an object is unused.
- Collection/backend: potentially database permissions/queries, repository or service metadata, schema versioning, extraction retention and pipeline changes. None is approved.
- Complexity/performance/storage: high and source-dependent; assess extraction load, snapshot growth, browser payload/memory and update cadence before a concrete proposal.
- Security/privacy: assess sensitive classifications, identity metadata, least privilege, deployment visibility and retention before collection.
- Compatibility/testing: require backward-compatible optional fields and mixed-version tests plus unavailable-permission/source states and collection integration tests. A revised proposal must specify the source and workload before acceptance.

## PEP-07 — Selectable catalog hierarchy

- Problem/persona: database developers repeatedly navigating adjacent objects must reconstruct server/database/schema context.
- UX/functionality: Gate 2 C shows a collapsible hierarchy beside the workspace. Object selection opens its existing route; selecting a hierarchy group must have explicitly specified semantics before implementation, rather than silently changing existing filters.
- Data/provenance: existing catalog identity fields; a deterministic grouping, not new source facts. No collection, backend, pipeline or catalog-schema changes are required.
- Complexity/performance/storage: medium; lazy expansion and bounded rendering are needed for large estates. Avoid mounting all object rows. Expansion state is ephemeral by default; persistence is a separate choice. No server storage.
- Security/privacy: existing catalog visibility only; no external transmission, extra permissions or inference.
- Compatibility: retain slash IDs, duplicate names in different contexts, schema-less/server-level objects and current routes/filters. A tree is optional navigation, not a replacement for complete matching results or export.
- Testing: group/object selection, collapse/expand, keyboard tree behavior, focus restoration, duplicate names, missing schema, many servers, 50,000-object fixture and direct deep links.
- Recommendation: P2; do not block the core refactor on a tree. Selecting C prompts detailed interaction review and does not silently approve all hierarchy features.

## PEP-08 — Global command navigation

- Problem/persona: repeat developer investigations involve frequent switching between known objects and Lineage/Explorer destinations.
- UX/functionality: Gate 2 D illustrates a command entry with object/destination suggestions and focused-Lineage action. Visible navigation remains available. It does not execute SQL or AI on focus. Shortcut, ranking, matching and dismissal behavior require specification before implementation.
- Data/provenance: existing catalog identity and route map, deterministically matched; no new collection, backend, pipeline or schema required. DDL filtering remains Explorer's existing capability unless separately approved.
- Complexity/performance/storage: medium; bounded suggestions, debounce and reuse of catalog data are necessary. Do not duplicate the entire catalog in a second index without measurements. No command history persistence or backend storage is included.
- Security/privacy: local matching only; do not send prompts, object names or searches externally or record new telemetry.
- Compatibility: preserve explicit routes, AI availability, focus encoding and visible fallback navigation. Browser/assistive-technology shortcut conflicts and focus restoration need review.
- Testing: exact/partial/duplicate names, no results, keyboard and pointer selection, Escape/focus return, long names, disabled AI, direct routes, empty catalog and large catalogs.
- Recommendation: P2; treat as optional ergonomics, not a prerequisite for context preservation. Selection of shell D alone is not implementation approval for this proposal.

## PEP-09 — Browse neighborhood list

- Problem/persona: developers need to scan and navigate the graph's selected objects without relying only on spatial layout.
- Proposed UX/functionality: a list beside Browse shows the same bounded selection, with separate Focus and Open object actions. It does not introduce another catalog search or change neighborhood filtering.
- Data/source/provenance: extracted object identities and the existing deterministic neighborhood selection. No new facts, heuristic scores or AI inference.
- Collection/backend/pipeline/storage: none; reuse current selection in memory, with no new persisted schema or index.
- Complexity/performance: medium; bounded or virtual rendering, shared selection calculation and no repeated traversal per row. Graph caps and summarization remain intact.
- Security/privacy: current catalog only, no new transmission or permission inference.
- Compatibility: preserve Browse/Access separation, focused-root retention, drill history, URLs, hops and graph exports. A list does not imply full-catalog results when the graph is bounded.
- Testing: filtered root, cycles, duplicate names, keyboard focus/open, empty selections, large and summarized selections, back/forward and selection consistency with graph.
- Recommendation: P2, optional L2; review concrete interaction before implementation.

## PEP-10 — Focus inspector

- Problem/persona: developers lose context when leaving Lineage to check nearby object identity or relationship evidence.
- Proposed UX/functionality: a dismissible inspector follows current focus and exposes existing identity and selected-edge column references. Single-click still drills; double-click still opens details. It is not a new independent selection model.
- Data/source/provenance: existing extracted metadata, relationship edges and best-effort column references; deterministic presentation only. Missing evidence remains unknown.
- Collection/backend/pipeline/storage: none; ephemeral panel visibility and current focus only, no additional collection or catalog fields.
- Complexity/performance: medium; reuse relationship indexes, avoid another graph layout, limit long lists and restore workspace width when closed.
- Security/privacy: current catalog visibility, no external processing or effective-permission calculation.
- Compatibility: retain edge details, focus navigation, URL semantics, bundling/member inspection, graph export and Access behavior. Narrow layouts require explicit drawer focus handling.
- Testing: drill/back consistency, closing/reopening, keyboard dismissal and focus restoration, absent metadata, remote targets, column references, summarized nodes, long identities and responsive graph sizing.
- Recommendation: P2, optional L3; model selection precedes detailed interaction approval.
