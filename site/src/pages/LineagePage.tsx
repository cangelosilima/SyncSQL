import { useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import { useCatalog } from '../lib/CatalogContext'
import LineageGraph from '../components/LineageGraph'
import FilterBar, { useFilteredNodes } from '../components/FilterBar'
import type { FilterToken } from '../lib/filters'

const GRAPH_CAP = 300

export default function LineagePage() {
  const { index } = useCatalog()
  const [searchParams] = useSearchParams()
  const focusId = searchParams.get('focus') ?? undefined
  const [tokens, setTokens] = useState<FilterToken[]>([])

  const allNodes = index?.catalog.nodes ?? []
  const filtered = useFilteredNodes(allNodes, tokens)
  const nodeIds = filtered.map((n) => n.id)

  if (!index) return null

  return (
    <div className="page page--wide">
      <h1>Lineage explorer</h1>
      <FilterBar nodes={allNodes} tokens={tokens} onChange={setTokens} placeholder="Filter the graph... (server, database, schema, type, name)" />
      <p className="muted" style={{ margin: '0.5rem 0' }}>
        {nodeIds.length} object(s) shown.{' '}
        {tokens.length === 0 && 'Start typing to narrow this down by server, database, schema or type.'}
      </p>

      {nodeIds.length > GRAPH_CAP ? (
        <p className="lineage-warning">
          {nodeIds.length} objects match this filter - add another filter (e.g. server or database) to keep the graph
          readable. Rendering more than {GRAPH_CAP} nodes at once gets slow and hard to read.
        </p>
      ) : (
        <LineageGraph nodeIds={nodeIds} focusId={focusId} height="70vh" />
      )}
    </div>
  )
}
