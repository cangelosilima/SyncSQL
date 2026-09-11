import { useEffect, useId, useMemo, useState } from 'react'
import { Link, useSearchParams } from 'react-router-dom'
import { useCatalog } from '../lib/CatalogContext'
import LineageGraph, { type EdgeColumnData } from '../components/LineageGraph'
import FilterBar, { useFilteredNodes } from '../components/FilterBar'
import ContentSearchBar from '../components/ContentSearchBar'
import TypeBadge from '../components/TypeBadge'
import CsvExportButton from '../components/CsvExportButton'
import HelpButton from '../components/HelpButton'
import InspectorPanel from '../components/InspectorPanel'
import RelatedObjects from '../components/RelatedObjects'
import { colorForType } from '../lib/typeColors'
import { getDirectionalNeighborhood, retainConnectingPaths } from '../lib/neighborhood'
import { findObjectsForGrantee, getSuggestedGrantees } from '../lib/grants'
import { filterByContent } from '../lib/contentSearch'
import { useDebouncedValue } from '../lib/useDebouncedValue'
import { groupRelated, type GroupBy } from '../lib/grouping'
import { csvFileName } from '../lib/csv'
import { objectGrantColumns, type ObjectGrantRow } from '../lib/catalogCsv'
import { decodeTokensFromUrl, encodeTokensForUrl, newTokenId, type FilterToken } from '../lib/filters'
import type { CatalogNode } from '../types'

const GRAPH_CAP = 300
const HOP_OPTIONS = [0, 1, 2, 3, 4, 5, 6] as const
function parseHops(value: string | null, fallback = 1): number {
  const number = value === null ? fallback : Number(value)
  return Number.isInteger(number) && number >= 0 && number <= 6 ? number : fallback
}
type Mode = 'browse' | 'access'

export default function LineagePage() {
  const { index } = useCatalog()
  const [searchParams, setSearchParams] = useSearchParams()
  const initialFocus = searchParams.get('focus') ?? undefined
  const initialGrantee = searchParams.get('grantee') ?? ''
  const initialFilterParam = searchParams.get('filter')
  const initialHops = parseHops(searchParams.get('hops'))
  const [mode, setMode] = useState<Mode>(searchParams.get('tab') === 'access' || initialGrantee ? 'access' : 'browse')
  const [copied, setCopied] = useState(false)
  const [edgeEvidence, setEdgeEvidence] = useState<EdgeColumnData | null>(null)

  // Browse-mode state. A ?filter=<encoded tokens> param (from a copied
  // shareable link - see the URL-sync effect below) is the only thing that
  // seeds tokens now.
  //
  // Arriving with ?focus=<id> deliberately seeds *no* token. It used to seed
  // "Name is <that object>", which read sensibly on its own but made every
  // filter added afterwards nonsense: the tokens are ANDed, so adding "Type
  // is StoredProcedures" while looking at a table asked for an object that is
  // both, and the graph emptied. Navigation identity belongs in focusStack;
  // clearFocus() seeds that name token at the moment focus is released, which
  // is what the seeding was actually for - not dumping the reader back into
  // the whole unfiltered catalog.
  const [tokens, setTokens] = useState<FilterToken[]>(() => decodeTokensFromUrl(initialFilterParam))
  const [focusStack, setFocusStack] = useState<string[]>(initialFocus ? [initialFocus] : [])
  const [dependencyHops, setDependencyHops] = useState(() => parseHops(searchParams.get('dependencies'), initialHops))
  const [dependentHops, setDependentHops] = useState(() => parseHops(searchParams.get('dependents'), initialHops))
  const [groupIntermediate, setGroupIntermediate] = useState(searchParams.get('group') === 'intermediate')
  const [contentQuery, setContentQuery] = useState(searchParams.get('q') ?? '')
  const debouncedContentQuery = useDebouncedValue(contentQuery, 150)

  // Access-mode state (merged from the standalone Access page - "what can
  // this grantee touch", now visualized in the same lineage graph).
  const [granteeQuery, setGranteeQuery] = useState(initialGrantee)
  const [exact, setExact] = useState(searchParams.get('exact') === '1')
  const [suggestionsOpen, setSuggestionsOpen] = useState(false)
  const [activeSuggestion, setActiveSuggestion] = useState(0)
  const suggestionsId = useId()
  const debouncedGrantee = useDebouncedValue(granteeQuery, 120)

  const allNodes = index?.catalog.nodes ?? []
  const attrFiltered = useFilteredNodes(allNodes, tokens)
  const filtered = useMemo(() => filterByContent(attrFiltered, debouncedContentQuery), [attrFiltered, debouncedContentQuery])
  const currentFocus = focusStack[focusStack.length - 1]
  useEffect(() => { setEdgeEvidence(null) }, [currentFocus, mode])

  const neighborhoodIds = useMemo(() => {
    if (!index || !currentFocus) return []
    return getDirectionalNeighborhood(index, currentFocus, dependencyHops, dependentHops)
  }, [index, currentFocus, dependencyHops, dependentHops])

  const grantSuggestions = useMemo(
    () => (granteeQuery.trim() ? getSuggestedGrantees(allNodes, granteeQuery, 20) : []),
    [allNodes, granteeQuery],
  )
  const grantMatches = useMemo(
    () => (mode === 'access' ? findObjectsForGrantee(allNodes, debouncedGrantee, exact) : []),
    [allNodes, debouncedGrantee, exact, mode],
  )
  const totalGrants = grantMatches.reduce((sum, m) => sum + m.grants.length, 0)
  const grantRows = useMemo<ObjectGrantRow[]>(
    () => grantMatches.flatMap(({ node, grants }) => grants.map((grant) => ({ node, grant }))),
    [grantMatches],
  )

  const baseIds = mode === 'access' ? grantMatches.map((m) => m.node.id) : filtered.map((n) => n.id)

  // While navigating a specific object, filters narrow *what is around it*
  // rather than re-selecting from the whole catalog. That is what someone
  // adding "Type is StoredProcedures" to a table's neighborhood is asking for
  // - show me the procedures near this - and the previous behaviour (drop the
  // focus, then AND the tokens against every node) answered a different
  // question with an empty graph. The focus object itself is always kept, so
  // the graph never renders rootless no matter how narrow the filter is.
  const nodeIds = useMemo(() => {
    if (!currentFocus) return baseIds
    if (mode !== 'browse') return neighborhoodIds
    const allowed = new Set(filtered.map((n) => n.id))
    return index ? retainConnectingPaths(index, currentFocus, neighborhoodIds, allowed) : []
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [index, currentFocus, mode, neighborhoodIds, filtered, baseIds.join(',')])

  const connectorIds = useMemo(() => {
    const matchingIds = new Set(filtered.map(node => node.id))
    return currentFocus && mode === 'browse' ? nodeIds.filter(id => id !== currentFocus && !matchingIds.has(id)) : []
  }, [currentFocus, mode, nodeIds, filtered])

  /** How much of the focused object's neighborhood the current filters are hiding. */
  const narrowedFrom = currentFocus ? neighborhoodIds.length : 0

  // Keeps the URL a live, shareable snapshot of the current Browse-mode
  // view (filter tokens, drill-down focus, hop radius, content search) -
  // copying the address bar reproduces this exact filtered graph for
  // incident write-ups or design docs. Access mode manages its own params
  // (tab/grantee) directly where it changes them.
  useEffect(() => {
    if (mode !== 'browse') return
    const params: Record<string, string> = {}
    if (tokens.length > 0) params.filter = encodeTokensForUrl(tokens)
    if (currentFocus) params.focus = currentFocus
    if (dependencyHops === dependentHops) {
      if (dependencyHops !== 1) params.hops = String(dependencyHops)
    } else {
      params.dependencies = String(dependencyHops)
      params.dependents = String(dependentHops)
    }
    if (groupIntermediate) params.group = 'intermediate'
    if (debouncedContentQuery.trim()) params.q = debouncedContentQuery
    setSearchParams(params, { replace: true })
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [mode, tokens, currentFocus, dependencyHops, dependentHops, groupIntermediate, debouncedContentQuery])

  function copyShareableLink() {
    navigator.clipboard
      .writeText(window.location.href)
      .then(() => {
        setCopied(true)
        setTimeout(() => setCopied(false), 1500)
      })
      .catch(() => {})
  }

  if (!index) return null

  function drillInto(id: string) {
    setFocusStack((prev) => (prev[prev.length - 1] === id ? prev : [...prev, id]))
  }

  function goToBreadcrumb(i: number) {
    setFocusStack((prev) => prev.slice(0, i + 1))
  }

  // Releasing focus keeps you on the object you were looking at rather than
  // dumping you into the whole catalog - by turning the navigation into a real
  // filter token at exactly the moment it stops being navigation. (Seeding
  // that token up front instead is what used to break every filter added
  // while navigating; see the tokens initializer.)
  function clearFocus() {
    const node = currentFocus ? index?.byId.get(currentFocus) : undefined
    if (node && !tokens.some((t) => t.attribute === 'name' && t.values.includes(node.qualifiedName))) {
      setTokens([...tokens, { id: newTokenId(), attribute: 'name', operator: 'is', values: [node.qualifiedName] }])
    }
    setFocusStack([])
  }

  function switchMode(next: Mode) {
    setMode(next)
    setFocusStack([])
    setSearchParams(next === 'access' ? { tab: 'access', ...(granteeQuery ? { grantee: granteeQuery } : {}), ...(exact ? { exact: '1' } : {}) } : {}, { replace: true })
  }

  function pickGrantee(value: string) {
    setGranteeQuery(value)
    setSearchParams({ tab: 'access', grantee: value, ...(exact ? { exact: '1' } : {}) }, { replace: true })
    setSuggestionsOpen(false)
  }

  return (
    <div className="page page--wide lineage-page">
      <div className="lineage-header-row">
        <h1 className="page-title">
          Lineage explorer
          <HelpButton topic="lineage" />
        </h1>
        <button type="button" className="lineage-share-btn" onClick={copyShareableLink}>
          {copied ? 'Copied!' : 'Copy link'}
        </button>
      </div>

      <div className="lineage-mode-tabs">
        <button type="button" aria-pressed={mode === 'browse'} className={mode === 'browse' ? 'lineage-mode-tab active' : 'lineage-mode-tab'} onClick={() => switchMode('browse')}>
          Browse
        </button>
        <button type="button" aria-pressed={mode === 'access'} className={mode === 'access' ? 'lineage-mode-tab active' : 'lineage-mode-tab'} onClick={() => switchMode('access')}>
          Access
        </button>
      </div>

      {mode === 'browse' ? (
        <>
          {/* Filters no longer clear the focus: while navigating an object they
              narrow its neighborhood, which is what someone filtering a
              drilled-into graph is asking for. The placeholder says which of
              the two is happening. */}
          <FilterBar
            nodes={allNodes}
            tokens={tokens}
            onChange={setTokens}
            placeholder={
              currentFocus
                ? 'Filter what surrounds this object... (server, database, schema, type, name)'
                : 'Filter the graph... (server, database, schema, type, name)'
            }
          />
          <ContentSearchBar value={contentQuery} onChange={setContentQuery} />
        </>
      ) : (
        <>
          <p className="muted" style={{ margin: '0.5rem 0' }}>
            Search by grantee (user, role or group) to see every object they have a GRANT or DENY permission on - down
            to the column when scoped that way - and how those objects relate to each other.
          </p>
          <div className="access-search">
            <div className="filter-bar" style={{ margin: 0, flex: 1 }}>
              <div className="filter-bar-input-row">
                <input
                  type="text"
                  className="filter-bar-input"
                  placeholder="Search grantee (user, role, group)..."
                  aria-label="Search grantee (user, role, group)"
                  role="combobox"
                  aria-autocomplete="list"
                  aria-expanded={suggestionsOpen && grantSuggestions.length > 0}
                  aria-controls={suggestionsId}
                  aria-activedescendant={suggestionsOpen && grantSuggestions[activeSuggestion] ? `${suggestionsId}-${activeSuggestion}` : undefined}
                  value={granteeQuery}
                  onChange={(e) => {
                    setGranteeQuery(e.target.value)
                    setSuggestionsOpen(true)
                    setActiveSuggestion(0)
                    setFocusStack([])
                    setSearchParams({ tab: 'access', ...(e.target.value ? { grantee: e.target.value } : {}), ...(exact ? { exact: '1' } : {}) }, { replace: true })
                  }}
                  onFocus={() => setSuggestionsOpen(true)}
                  onKeyDown={(event) => {
                    if (event.key === 'Escape') { setSuggestionsOpen(false); return }
                    if (!grantSuggestions.length) return
                    if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
                      event.preventDefault(); setSuggestionsOpen(true)
                      setActiveSuggestion(i => (i + (event.key === 'ArrowDown' ? 1 : -1) + grantSuggestions.length) % grantSuggestions.length)
                    } else if (event.key === 'Enter' && suggestionsOpen && grantSuggestions[activeSuggestion]) {
                      event.preventDefault(); pickGrantee(grantSuggestions[activeSuggestion])
                    }
                  }}
                  onBlur={() => setTimeout(() => setSuggestionsOpen(false), 150)}
                />
              </div>
              {suggestionsOpen && grantSuggestions.length > 0 && (
                <ul className="filter-suggestions" role="listbox" id={suggestionsId} aria-label="Grantees">
                  {grantSuggestions.map((s, i) => (
                    <li
                      key={s}
                      role="option"
                      id={`${suggestionsId}-${i}`}
                      aria-selected={i === activeSuggestion}
                      className="filter-suggestion"
                      onMouseDown={(e) => {
                        e.preventDefault()
                        pickGrantee(s)
                      }}
                    >
                      {s}
                    </li>
                  ))}
                </ul>
              )}
            </div>
            <label className="access-exact-toggle">
              <input type="checkbox" checked={exact} onChange={(e) => {
                setExact(e.target.checked)
                setSearchParams({ tab: 'access', ...(granteeQuery ? { grantee: granteeQuery } : {}), ...(e.target.checked ? { exact: '1' } : {}) }, { replace: true })
              }} />
              Exact match
            </label>
          </div>

          {debouncedGrantee.trim() && (
            <p className="muted" style={{ margin: '0.75rem 0 0.25rem' }}>
              {grantMatches.length === 0
                ? `No grants found for "${debouncedGrantee}".`
                : `${totalGrants} grant${totalGrants === 1 ? '' : 's'} across ${grantMatches.length} object${grantMatches.length === 1 ? '' : 's'} matching "${debouncedGrantee}" - shown below and in the graph.`}
            </p>
          )}

          {grantMatches.length > 0 && (
            <>
            <div className="lineage-header-row" style={{ margin: '0.5rem 0' }}>
              <span className="muted">One row per object, one CSV row per permission.</span>
              <CsvExportButton
                rows={grantRows}
                columns={objectGrantColumns}
                filename={csvFileName('syncsql-access', debouncedGrantee)}
              />
            </div>
            <div className="explorer-table-wrap" style={{ marginBottom: '0.75rem' }}>
              <table className="explorer-table">
                <thead>
                  <tr>
                    <th>Object</th>
                    <th>Type</th>
                    <th>Server</th>
                    <th>Database</th>
                    <th>Permissions</th>
                  </tr>
                </thead>
                <tbody>
                  {grantMatches.map(({ node, grants }) => (
                    <tr key={node.id}>
                      <td>
                        <Link to={`/object/${node.id}`}>{node.qualifiedName}</Link>
                      </td>
                      <td>
                        <TypeBadge type={node.type} />
                      </td>
                      <td>{node.server}</td>
                      <td>{node.database}</td>
                      <td>
                        <span className="column-tags">
                          {grants.map((g, i) => (
                            <span
                              key={`${g.grantee}-${g.permission}-${g.column ?? ''}-${i}`}
                              className={g.state === 'DENY' ? 'column-tag column-tag--deny' : 'column-tag'}
                            >
                              {g.permission}
                              {g.column ? `(${g.column})` : ''}
                              {g.state === 'DENY' ? ' [DENY]' : ''}
                            </span>
                          ))}
                        </span>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
            </>
          )}
        </>
      )}

      {currentFocus ? (
        <div className="lineage-nav">
          <span className="lineage-nav-label">Navigating:</span>
          <ol className="breadcrumb-trail">
            {focusStack.map((id, i) => {
              const n = index.byId.get(id)
              const isLast = i === focusStack.length - 1
              return (
                <li key={`${id}-${i}`}>
                  {isLast ? (
                    <span className="breadcrumb-current">{n?.qualifiedName ?? id}</span>
                  ) : (
                    <button type="button" className="breadcrumb-link" onClick={() => goToBreadcrumb(i)}>
                      {n?.qualifiedName ?? id}
                    </button>
                  )}
                  {!isLast && <span className="breadcrumb-sep">&rsaquo;</span>}
                </li>
              )
            })}
          </ol>
          <button
            type="button"
            className="lineage-nav-back"
            onClick={() => setFocusStack((prev) => prev.slice(0, -1))}
            disabled={focusStack.length <= 1}
          >
            &larr; Back
          </button>
          <button type="button" className="lineage-nav-clear" onClick={clearFocus}>
            Clear focus
          </button>
        </div>
      ) : null}

      {currentFocus && <details className="lineage-view-options" open>
        <summary>View options</summary>
        <div className="lineage-view-controls">
          {([
            ['Dependencies', dependencyHops, setDependencyHops],
            ['Dependents', dependentHops, setDependentHops],
          ] as const).map(([label, value, setValue]) => <div className="lineage-hop-control" key={label}>
            <label className="lineage-hops">
              {label}
              <select aria-label={`${label} hops`} value={value} onChange={event => setValue(Number(event.target.value))}>
                {HOP_OPTIONS.map(hop => <option key={hop} value={hop}>{hop} hop{hop === 1 ? '' : 's'}</option>)}
              </select>
            </label>
            <button type="button" className="lineage-nav-back" disabled={value >= 6} onClick={() => setValue(value + 1)} aria-label={`Add ${label.toLowerCase()} hop`}>+ Add hop</button>
          </div>)}
          <label className="lineage-group-toggle">
            <input type="checkbox" checked={groupIntermediate} onChange={event => setGroupIntermediate(event.target.checked)} />
            Group intermediate layers
          </label>
        </div>
        <p className="muted">Dependencies are objects the focus references; dependents are objects that reference it. Set either direction to 0 to hide it. Grouping combines intermediate objects of the same type and hop, keeping the focus and outer objects visible.</p>
      </details>}

      {currentFocus ? (
        <p className="muted" style={{ margin: '0.5rem 0' }}>
          {nodeIds.length < narrowedFrom
            ? `Showing ${nodeIds.length} of ${narrowedFrom} objects around ${index.byId.get(currentFocus)?.qualifiedName ?? currentFocus} - the filters above narrow this neighborhood, not the whole catalog.`
            : `${nodeIds.length} object(s) around ${index.byId.get(currentFocus)?.qualifiedName ?? currentFocus}. Filter above to narrow this neighborhood, or clear the focus to search the whole catalog.`}
        </p>
      ) : (
        <p className="muted" style={{ margin: '0.5rem 0' }}>
          {nodeIds.length} object(s) shown.{' '}
          {mode === 'browse' && tokens.length === 0 && 'Start typing to narrow this down by server, database, schema or type. '}
          {mode === 'access' && !debouncedGrantee.trim() && 'Start typing a grantee name to search. '}
          {nodeIds.length > 0 && 'Click any node to drill into its own dependencies/dependents; double-click to open its full detail page.'}
        </p>
      )}

      {connectorIds.length > 0 && <p className="muted lineage-connector-hint">{connectorIds.length} connecting object{connectorIds.length === 1 ? '' : 's'} kept outside the filters to preserve paths to matching objects.</p>}

      <div className="investigation-layout">
      <div className="workspace-content graph-workspace">
      {nodeIds.length > GRAPH_CAP ? (
        <SelectionBreakdown
          nodes={nodeIds.map((id) => index.byId.get(id)).filter((n): n is CatalogNode => Boolean(n))}
          onNarrow={
            // Access mode's selection comes from the grantee search, not from
            // filter tokens, so there'd be nothing for a click to narrow there.
            mode === 'browse'
              ? (attribute, value) => {
                  setTokens([...tokens, { id: newTokenId(), attribute, operator: 'is', values: [value] }])
                }
              : undefined
          }
        />
      ) : (
        <>
        <details className="lineage-legend" open>
          <summary>Graph legend</summary>
          <ul>
            <li><span className="legend-arrow" aria-hidden="true">→</span>Referencing object → referenced object</li>
            <li><span className="legend-line" aria-hidden="true" />Reference</li>
            <li><span className="legend-line legend-line--columns" aria-hidden="true" />Labeled edge: recorded column references</li>
            <li><span className="legend-line legend-line--dynamic" aria-hidden="true" />Dashed edge: dynamic SQL reference (weaker evidence)</li>
            <li><span className="legend-node legend-node--focus" aria-hidden="true" />Current focus</li>
            <li><span className="legend-node legend-node--group" aria-hidden="true" />Dashed node: grouped objects</li>
          </ul>
          <p className="muted">Node border color indicates object type. Column references are best-effort evidence detected from SQL, not complete column lineage.</p>
          <ul aria-label="Object types in this graph">
            {[...new Set(nodeIds.map(id => index.byId.get(id)?.type).filter((type): type is string => Boolean(type)))].sort().map(type => (
              <li key={type}><span className="legend-node" style={{ borderColor: colorForType(type) }} aria-hidden="true" />{type}</li>
            ))}
          </ul>
        </details>
        <LineageGraph nodeIds={nodeIds} focusId={currentFocus} height="70vh" onNodeActivate={drillInto} onEdgeInspect={setEdgeEvidence} groupIntermediate={groupIntermediate} connectorIds={connectorIds} />
        </>
      )}
      </div>
      <InspectorPanel>
        {edgeEvidence && nodeIds.length <= GRAPH_CAP && <section aria-label="Edge evidence">
          <h3>Edge evidence</h3>
          <p>{index.byId.get(edgeEvidence.from)?.qualifiedName ?? edgeEvidence.from} → {index.byId.get(edgeEvidence.to)?.qualifiedName ?? edgeEvidence.to}</p>
          <p className="muted">Known column references detected in DDL; best-effort evidence, not certified column-level lineage.</p>
          <div className="column-tags">{edgeEvidence.columns.map(column => <span key={column} className="column-tag">{column}</span>)}</div>
          {edgeEvidence.dynamic && <p className="muted">Recovered from dynamically built SQL.</p>}
        </section>}
        {currentFocus && index.byId.has(currentFocus) ? <>
          <h3 className="inspector-identity">{index.byId.get(currentFocus)!.qualifiedName}</h3>
          <TypeBadge type={index.byId.get(currentFocus)!.type} />
          <p className="muted">{index.byId.get(currentFocus)!.server} → {index.byId.get(currentFocus)!.database}</p>
          <Link to={`/object/${currentFocus}`}>Open object workbench →</Link>
          <RelatedObjects title="Depends on" rootId={currentFocus} ids={index.outgoing.get(currentFocus) ?? []} direction="outgoing" />
          <RelatedObjects title="Used by" rootId={currentFocus} ids={index.incoming.get(currentFocus) ?? []} direction="incoming" />
          {mode === 'access' && <><h3>Recorded permissions</h3>
            <p className="muted">Current object grants, not an effective-access calculation.</p>
            {index.byId.get(currentFocus)!.grants.length === 0 && <p>No grants recorded.</p>}
            <ul className="permission-list">{index.byId.get(currentFocus)!.grants.map((grant, i) => <li key={i}>
              <button type="button" className="breadcrumb-link" onClick={() => { setFocusStack([]); pickGrantee(grant.grantee) }}>{grant.grantee}</button>
              <span className={`grant-state grant-state--${grant.state === 'DENY' ? 'deny' : 'grant'}`}>{grant.state}</span> {grant.permission} · {grant.column ?? 'whole object'}
            </li>)}</ul></>}
        </> : <p className="empty-state">Drill into a graph node to inspect its identity and relationships. Edge details appear beside the selected relationship in the graph.</p>}
      </InspectorPanel>
      </div>
    </div>
  )
}

/** The grouping axes offered as a way out of an over-sized selection - the ones a filter token can narrow on. */
const BREAKDOWN_AXES: { by: GroupBy; attribute: 'server' | 'database' | 'type'; label: string }[] = [
  { by: 'server', attribute: 'server', label: 'By server' },
  { by: 'database', attribute: 'database', label: 'By database' },
  { by: 'type', attribute: 'type', label: 'By type' },
]

/** Rows past this per axis are summarized as "+N more" - the point is to pick one, not to read them all. */
const BREAKDOWN_ROWS = 8

/**
 * What a too-large selection is actually made of, with every row a one-click
 * narrowing. Drawing 4,000 nodes helps nobody, but "3,812 objects: 3,100 of them
 * Tables in WarehouseDb" is the answer to the question that got someone here -
 * and clicking that row is the next step, instead of a warning that just stops.
 */
function SelectionBreakdown({
  nodes,
  onNarrow,
}: {
  nodes: CatalogNode[]
  onNarrow?: (attribute: 'server' | 'database' | 'type', value: string) => void
}) {
  return (
    <div className="lineage-warning selection-breakdown">
      <p>
        <strong>{nodes.length} objects</strong> match this selection - too many to draw as one graph, and unreadable if
        we did. Here is what they are
        {onNarrow ? '; pick a row to narrow the filter, or drill into a single object from the Explorer.' : '.'}
      </p>
      <div className="selection-breakdown-axes">
        {BREAKDOWN_AXES.map(({ by, attribute, label }) => {
          const groups = groupRelated(nodes, by)
          const shown = groups.slice(0, BREAKDOWN_ROWS)
          const rest = groups.length - shown.length
          return (
            <div key={by} className="selection-breakdown-axis">
              <h4>{label}</h4>
              <ul>
                {shown.map((group) =>
                  onNarrow ? (
                    <li key={group.key}>
                      <button type="button" className="selection-breakdown-row" onClick={() => onNarrow(attribute, group.key)}>
                        <span className="selection-breakdown-name">{group.key}</span>
                        <span className="selection-breakdown-count">{group.nodes.length}</span>
                      </button>
                    </li>
                  ) : (
                    <li key={group.key}>
                      <span className="selection-breakdown-row selection-breakdown-row--static">
                        <span className="selection-breakdown-name">{group.key}</span>
                        <span className="selection-breakdown-count">{group.nodes.length}</span>
                      </span>
                    </li>
                  ),
                )}
                {rest > 0 && (
                  <li className="muted selection-breakdown-rest">
                    +{rest} more {by}
                    {rest === 1 ? '' : 's'}
                  </li>
                )}
              </ul>
            </div>
          )
        })}
      </div>
    </div>
  )
}
