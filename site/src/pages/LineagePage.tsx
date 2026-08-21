import { useMemo, useState } from 'react'
import { Link, useSearchParams } from 'react-router-dom'
import { useCatalog } from '../lib/CatalogContext'
import LineageGraph from '../components/LineageGraph'
import FilterBar, { useFilteredNodes } from '../components/FilterBar'
import TypeBadge from '../components/TypeBadge'
import { getNeighborhoodIds } from '../lib/neighborhood'
import { findObjectsForGrantee, getSuggestedGrantees } from '../lib/grants'
import { useDebouncedValue } from '../lib/useDebouncedValue'
import type { FilterToken } from '../lib/filters'

const GRAPH_CAP = 300
const HOP_OPTIONS = [1, 2, 3] as const
type Mode = 'browse' | 'access'

export default function LineagePage() {
  const { index } = useCatalog()
  const [searchParams, setSearchParams] = useSearchParams()
  const initialFocus = searchParams.get('focus') ?? undefined
  const initialGrantee = searchParams.get('grantee') ?? ''
  const [mode, setMode] = useState<Mode>(searchParams.get('tab') === 'access' || initialGrantee ? 'access' : 'browse')

  // Browse-mode state. Arriving with ?focus=<id> (from an object page's
  // "Open in full lineage explorer" link) both focuses that object AND
  // seeds a real filter token for it, so clearing focus lands on "just this
  // object" rather than dumping back out to the whole unfiltered catalog.
  const [tokens, setTokens] = useState<FilterToken[]>(() => {
    if (!initialFocus) return []
    const node = index?.byId.get(initialFocus)
    return node ? [{ id: 'seed-focus', attribute: 'name', operator: 'is', values: [node.qualifiedName] }] : []
  })
  const [focusStack, setFocusStack] = useState<string[]>(initialFocus ? [initialFocus] : [])
  const [hops, setHops] = useState<(typeof HOP_OPTIONS)[number]>(1)

  // Access-mode state (merged from the standalone Access page - "what can
  // this grantee touch", now visualized in the same lineage graph).
  const [granteeQuery, setGranteeQuery] = useState(initialGrantee)
  const [exact, setExact] = useState(false)
  const [suggestionsOpen, setSuggestionsOpen] = useState(false)
  const debouncedGrantee = useDebouncedValue(granteeQuery, 120)

  const allNodes = index?.catalog.nodes ?? []
  const filtered = useFilteredNodes(allNodes, tokens)
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

  const baseIds = mode === 'access' ? grantMatches.map((m) => m.node.id) : filtered.map((n) => n.id)
  const nodeIds = currentFocus ? neighborhoodIds : baseIds

  if (!index) return null

  function drillInto(id: string) {
    setFocusStack((prev) => (prev[prev.length - 1] === id ? prev : [...prev, id]))
  }

  function goToBreadcrumb(i: number) {
    setFocusStack((prev) => prev.slice(0, i + 1))
  }

  function clearFocus() {
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
      <h1>Lineage explorer</h1>

      <div className="lineage-mode-tabs">
        <button type="button" className={mode === 'browse' ? 'lineage-mode-tab active' : 'lineage-mode-tab'} onClick={() => switchMode('browse')}>
          Browse
        </button>
        <button type="button" className={mode === 'access' ? 'lineage-mode-tab active' : 'lineage-mode-tab'} onClick={() => switchMode('access')}>
          Access
        </button>
      </div>

      {mode === 'browse' ? (
        <FilterBar
          nodes={allNodes}
          tokens={tokens}
          onChange={(next) => {
            setTokens(next)
            setFocusStack([])
          }}
          placeholder="Filter the graph... (server, database, schema, type, name)"
        />
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
      ) : (
        <p className="muted" style={{ margin: '0.5rem 0' }}>
          {nodeIds.length} object(s) shown.{' '}
          {mode === 'browse' && tokens.length === 0 && 'Start typing to narrow this down by server, database, schema or type. '}
          {mode === 'access' && !debouncedGrantee.trim() && 'Start typing a grantee name to search. '}
          {nodeIds.length > 0 && 'Click any node to drill into its own dependencies/dependents; double-click to open its full detail page.'}
        </p>
      )}

      {nodeIds.length > GRAPH_CAP ? (
        <p className="lineage-warning">
          {nodeIds.length} objects match this filter - add another filter (e.g. server or database) to keep the graph
          readable. Rendering more than {GRAPH_CAP} nodes at once gets slow and hard to read.
        </p>
      ) : (
        <LineageGraph nodeIds={nodeIds} focusId={currentFocus} height="70vh" onNodeActivate={drillInto} />
      )}
    </div>
  )
}
