import { useMemo, useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import { useCatalog } from '../lib/CatalogContext'
import LineageGraph from '../components/LineageGraph'
import FilterBar, { useFilteredNodes } from '../components/FilterBar'
import { getNeighborhoodIds } from '../lib/neighborhood'
import type { FilterToken } from '../lib/filters'

const GRAPH_CAP = 300
const HOP_OPTIONS = [1, 2, 3] as const

export default function LineagePage() {
  const { index } = useCatalog()
  const [searchParams] = useSearchParams()
  const initialFocus = searchParams.get('focus') ?? undefined
  const [tokens, setTokens] = useState<FilterToken[]>([])
  const [focusStack, setFocusStack] = useState<string[]>(initialFocus ? [initialFocus] : [])
  const [hops, setHops] = useState<(typeof HOP_OPTIONS)[number]>(1)

  const allNodes = index?.catalog.nodes ?? []
  const filtered = useFilteredNodes(allNodes, tokens)
  const currentFocus = focusStack[focusStack.length - 1]

  const neighborhoodIds = useMemo(() => {
    if (!index || !currentFocus) return []
    return getNeighborhoodIds(index, currentFocus, hops)
  }, [index, currentFocus, hops])

  const nodeIds = currentFocus ? neighborhoodIds : filtered.map((n) => n.id)

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

  return (
    <div className="page page--wide">
      <h1>Lineage explorer</h1>
      <FilterBar
        nodes={allNodes}
        tokens={tokens}
        onChange={(next) => {
          setTokens(next)
          setFocusStack([])
        }}
        placeholder="Filter the graph... (server, database, schema, type, name)"
      />

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
            Clear &amp; show filtered graph
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
          {tokens.length === 0 && 'Start typing to narrow this down by server, database, schema or type. '}
          Click any node to drill into its own dependencies/dependents; double-click to open its full detail page.
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
