import { useEffect, useMemo, useState } from 'react'
import { Link, useSearchParams } from 'react-router-dom'
import { useCatalog } from '../lib/CatalogContext'
import LineageGraph from '../components/LineageGraph'
import FilterBar, { useFilteredNodes } from '../components/FilterBar'
import ContentSearchBar from '../components/ContentSearchBar'
import TypeBadge from '../components/TypeBadge'
import CsvExportButton from '../components/CsvExportButton'
import HelpButton from '../components/HelpButton'
import { getNeighborhoodIds } from '../lib/neighborhood'
import { findObjectsForGrantee, getSuggestedGrantees } from '../lib/grants'
import { filterByContent } from '../lib/contentSearch'
import { useDebouncedValue } from '../lib/useDebouncedValue'
import { groupRelated, type GroupBy } from '../lib/grouping'
import { csvFileName } from '../lib/csv'
import { objectGrantColumns, type ObjectGrantRow } from '../lib/catalogCsv'
import { decodeTokensFromUrl, encodeTokensForUrl, newTokenId, type FilterToken } from '../lib/filters'
import type { CatalogNode } from '../types'

const GRAPH_CAP = 300
const HOP_OPTIONS = [1, 2, 3] as const
type Mode = 'browse' | 'access'

export default function LineagePage() {
  const { index } = useCatalog()
  const [searchParams, setSearchParams] = useSearchParams()
  const initialFocus = searchParams.get('focus') ?? undefined
  const initialGrantee = searchParams.get('grantee') ?? ''
  const initialFilterParam = searchParams.get('filter')
  const initialHops = Number(searchParams.get('hops') ?? '1')
  const [mode, setMode] = useState<Mode>(searchParams.get('tab') === 'access' || initialGrantee ? 'access' : 'browse')
  const [copied, setCopied] = useState(false)

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
  const [hops, setHops] = useState<(typeof HOP_OPTIONS)[number]>(
    (HOP_OPTIONS as readonly number[]).includes(initialHops) ? (initialHops as (typeof HOP_OPTIONS)[number]) : 1,
  )
  const [contentQuery, setContentQuery] = useState(searchParams.get('q') ?? '')
  const debouncedContentQuery = useDebouncedValue(contentQuery, 150)

  // Access-mode state (merged from the standalone Access page - "what can
  // this grantee touch", now visualized in the same lineage graph).
  const [granteeQuery, setGranteeQuery] = useState(initialGrantee)
  const [exact, setExact] = useState(false)
  const [suggestionsOpen, setSuggestionsOpen] = useState(false)
  const debouncedGrantee = useDebouncedValue(granteeQuery, 120)

  const allNodes = index?.catalog.nodes ?? []
  const attrFiltered = useFilteredNodes(allNodes, tokens)
  const filtered = useMemo(() => filterByContent(attrFiltered, debouncedContentQuery), [attrFiltered, debouncedContentQuery])
  const currentFocus = focusStack[focusStack.length - 1]

  const neighborhoodIds = useMemo(() => {
    if (!index || !currentFocus) return []
    return getNeighborhoodIds(index, currentFocus, hops)
  }, [index, currentFocus, hops])

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
    return neighborhoodIds.filter((id) => id === currentFocus || allowed.has(id))
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [currentFocus, mode, neighborhoodIds, filtered, baseIds.join(',')])

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
    if (hops !== 1) params.hops = String(hops)
    if (debouncedContentQuery.trim()) params.q = debouncedContentQuery
    setSearchParams(params, { replace: true })
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [mode, tokens, currentFocus, hops, debouncedContentQuery])

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
    setSearchParams(next === 'access' ? { tab: 'access', ...(granteeQuery ? { grantee: granteeQuery } : {}) } : {}, { replace: true })
  }

  function pickGrantee(value: string) {
    setGranteeQuery(value)
    setSearchParams({ tab: 'access', grantee: value }, { replace: true })
    setSuggestionsOpen(false)
  }

  return (
    <div className="page page--wide">
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
        <button type="button" className={mode === 'browse' ? 'lineage-mode-tab active' : 'lineage-mode-tab'} onClick={() => switchMode('browse')}>
          Browse
        </button>
        <button type="button" className={mode === 'access' ? 'lineage-mode-tab active' : 'lineage-mode-tab'} onClick={() => switchMode('access')}>
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
                  value={granteeQuery}
                  onChange={(e) => {
                    setGranteeQuery(e.target.value)
                    setSuggestionsOpen(true)
                    setFocusStack([])
                    setSearchParams(e.target.value ? { tab: 'access', grantee: e.target.value } : { tab: 'access' }, { replace: true })
                  }}
                  onFocus={() => setSuggestionsOpen(true)}
                  onBlur={() => setTimeout(() => setSuggestionsOpen(false), 150)}
                />
              </div>
              {suggestionsOpen && grantSuggestions.length > 0 && (
                <ul className="filter-suggestions" role="listbox">
                  {grantSuggestions.map((s) => (
                    <li
                      key={s}
                      role="option"
                      aria-selected={false}
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
              <input type="checkbox" checked={exact} onChange={(e) => setExact(e.target.checked)} />
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
          <label className="lineage-hops">
            Radius
            <select value={hops} onChange={(e) => setHops(Number(e.target.value) as (typeof HOP_OPTIONS)[number])}>
              {HOP_OPTIONS.map((h) => (
                <option key={h} value={h}>
                  {h} hop{h === 1 ? '' : 's'}
                </option>
              ))}
            </select>
          </label>
        </div>
      ) : null}

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
        <LineageGraph nodeIds={nodeIds} focusId={currentFocus} height="70vh" onNodeActivate={drillInto} />
      )}
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
