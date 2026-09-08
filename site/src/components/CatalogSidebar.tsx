import { useMemo, useState } from 'react'
import { NavLink } from 'react-router-dom'
import type { CatalogNode } from '../types'
import Button from './Button'

const BRANCH_CAP = 100
const keys = ['server', 'database', 'schema', 'type'] as const

function Branches({ nodes, level = 0 }: { nodes: CatalogNode[]; level?: number }) {
  const [limit, setLimit] = useState(BRANCH_CAP)
  const groups = useMemo(() => {
    const result = new Map<string, { name: string; nodes: CatalogNode[]; level: number }>()
    if (level < keys.length) for (const node of nodes) {
      const groupLevel = level === 2 && !node.schema?.trim() ? 3 : level
      const name = node[keys[groupLevel]] ?? '(unknown)'
      const key = groupLevel + ':' + name
      if (!result.has(key)) result.set(key, { name, nodes: [], level: groupLevel })
      result.get(key)!.nodes.push(node)
    }
    return [...result].sort(([a], [b]) => a.localeCompare(b))
  }, [nodes, level])
  const sorted = useMemo(() => nodes.filter(() => level === keys.length).sort((a, b) => a.qualifiedName.localeCompare(b.qualifiedName)), [nodes, level])
  const total = sorted.length + groups.length
  return <ul className="catalog-branches">
    {groups.slice(0, limit).map(([key, group]) => <Branch key={key} name={group.name} nodes={group.nodes} level={group.level} />)}
    {sorted.slice(0, Math.max(0, limit - groups.length)).map(node => <li key={node.id}>
      <NavLink to={`/object/${node.id}`} title={`${node.qualifiedName} · ${node.type}`}><span>{node.qualifiedName}</span><small>{node.type}</small></NavLink>
    </li>)}
    {total > limit && <li><Button onClick={() => setLimit(n => n + BRANCH_CAP)}>Show more ({total - limit})</Button></li>}
  </ul>
}

function Branch({ name, nodes, level }: { name: string; nodes: CatalogNode[]; level: number }) {
  const [open, setOpen] = useState(false)
  return <li><button className="catalog-branch" type="button" aria-expanded={open} onClick={() => setOpen(v => !v)}>
    <span aria-hidden="true">{open ? '▾' : '▸'}</span><span>{name}</span><small>{nodes.length}</small>
  </button>{open && <Branches nodes={nodes} level={level + 1} />}</li>
}

export default function CatalogSidebar({ nodes }: { nodes: CatalogNode[] }) {
  return <aside className="catalog-sidebar" aria-label="Catalog">
    <h2>Catalog</h2>
    <p className="muted">Browse objects by server, database, schema and type.</p>
    <nav aria-label="Catalog objects"><Branches nodes={nodes} /></nav>
    {nodes.length === 0 && <p className="muted">No objects in this snapshot.</p>}
  </aside>
}
