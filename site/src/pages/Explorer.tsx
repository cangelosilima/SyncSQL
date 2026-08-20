import { useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import { useCatalog } from '../lib/CatalogContext'
import FilterBar, { useFilteredNodes } from '../components/FilterBar'
import TypeBadge from '../components/TypeBadge'
import type { CatalogNode } from '../types'
import type { FilterToken } from '../lib/filters'

type SortKey = 'qualifiedName' | 'type' | 'server' | 'database' | 'schema' | 'lastChangedAt' | 'changeCount'

const ROW_CAP = 500

export default function Explorer() {
  const { index } = useCatalog()
  const [tokens, setTokens] = useState<FilterToken[]>([])
  const [sortKey, setSortKey] = useState<SortKey>('qualifiedName')
  const [sortDir, setSortDir] = useState<1 | -1>(1)

  const nodes = index?.catalog.nodes ?? []
  const filtered = useFilteredNodes(nodes, tokens)

  const sorted = useMemo(() => {
    const copy = [...filtered]
    copy.sort((a, b) => {
      const av = sortValue(a, sortKey)
      const bv = sortValue(b, sortKey)
      if (av < bv) return -1 * sortDir
      if (av > bv) return 1 * sortDir
      return 0
    })
    return copy
  }, [filtered, sortKey, sortDir])

  if (!index) return null

  const visible = sorted.slice(0, ROW_CAP)

  function toggleSort(key: SortKey) {
    if (key === sortKey) {
      setSortDir((d) => (d === 1 ? -1 : 1))
    } else {
      setSortKey(key)
      setSortDir(1)
    }
  }

  return (
    <div className="page page--wide">
      <h1>Explorer</h1>
      <p className="muted">
        {filtered.length} of {nodes.length} object(s) match{tokens.length > 0 ? ' the current filter' : ''}.
      </p>
      <FilterBar nodes={nodes} tokens={tokens} onChange={setTokens} placeholder="Filter objects... (server, database, schema, type, name, description)" />

      {sorted.length > ROW_CAP && (
        <p className="lineage-warning" style={{ marginTop: '0.75rem' }}>
          Showing the first {ROW_CAP} of {sorted.length} matching objects - narrow your filter to see the rest.
        </p>
      )}

      <div className="explorer-table-wrap">
        <table className="explorer-table">
          <thead>
            <tr>
              <SortableHeader label="Object" sortKey="qualifiedName" active={sortKey} dir={sortDir} onClick={toggleSort} />
              <SortableHeader label="Type" sortKey="type" active={sortKey} dir={sortDir} onClick={toggleSort} />
              <SortableHeader label="Server" sortKey="server" active={sortKey} dir={sortDir} onClick={toggleSort} />
              <SortableHeader label="Database" sortKey="database" active={sortKey} dir={sortDir} onClick={toggleSort} />
              <SortableHeader label="Schema" sortKey="schema" active={sortKey} dir={sortDir} onClick={toggleSort} />
              <th>Description</th>
              <SortableHeader label="Changes" sortKey="changeCount" active={sortKey} dir={sortDir} onClick={toggleSort} />
              <SortableHeader label="Last changed" sortKey="lastChangedAt" active={sortKey} dir={sortDir} onClick={toggleSort} />
            </tr>
          </thead>
          <tbody>
            {visible.map((node) => (
              <tr key={node.id}>
                <td>
                  <Link to={`/object/${node.id}`}>{node.qualifiedName}</Link>
                </td>
                <td>
                  <TypeBadge type={node.type} />
                </td>
                <td>{node.server}</td>
                <td>{node.database}</td>
                <td>{node.schema ?? <span className="muted">-</span>}</td>
                <td className="explorer-description">{node.description ?? <span className="muted">-</span>}</td>
                <td>{node.changeCount || <span className="muted">0</span>}</td>
                <td>{node.lastChangedAt ? new Date(node.lastChangedAt).toLocaleDateString() : <span className="muted">-</span>}</td>
              </tr>
            ))}
          </tbody>
        </table>
        {visible.length === 0 && <p className="sidebar-empty" style={{ padding: '1rem' }}>No objects match this filter.</p>}
      </div>
    </div>
  )
}

function sortValue(node: CatalogNode, key: SortKey): string | number {
  switch (key) {
    case 'qualifiedName':
      return node.qualifiedName.toLowerCase()
    case 'type':
      return node.type.toLowerCase()
    case 'server':
      return node.server.toLowerCase()
    case 'database':
      return node.database.toLowerCase()
    case 'schema':
      return (node.schema ?? '').toLowerCase()
    case 'lastChangedAt':
      return node.lastChangedAt ?? ''
    case 'changeCount':
      return node.changeCount
  }
}

function SortableHeader({
  label,
  sortKey,
  active,
  dir,
  onClick,
}: {
  label: string
  sortKey: SortKey
  active: SortKey
  dir: 1 | -1
  onClick: (key: SortKey) => void
}) {
  const isActive = active === sortKey
  return (
    <th>
      <button type="button" className="explorer-sort-btn" onClick={() => onClick(sortKey)}>
        {label}
        {isActive && <span className="explorer-sort-arrow">{dir === 1 ? ' ▲' : ' ▼'}</span>}
      </button>
    </th>
  )
}
