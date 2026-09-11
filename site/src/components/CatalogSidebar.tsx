import { useMemo, useState } from 'react'
import { NavLink } from 'react-router-dom'
import type { CatalogLinkedServerReference, CatalogNode } from '../types'
import { isLinkNode } from '../lib/catalog'
import Button from './Button'

const BRANCH_CAP = 100
const keys = ['server', 'database', 'schema', 'type'] as const
const NO_LINK_REFERENCES: CatalogLinkedServerReference[] = []

function engineLabel(engine?: string | null) {
  const value = engine?.trim()
  return value?.toLowerCase() === 'mssql' ? 'SQL Server' : value?.toLowerCase() === 'oracle' ? 'Oracle' : value
}

function Branches({ nodes, linkEngines, level = 0 }: { nodes: CatalogNode[]; linkEngines: Map<string, string>; level?: number }) {
  const [limit, setLimit] = useState(BRANCH_CAP)
  const groups = useMemo(() => {
    const result = new Map<string, { name: string; nodes: CatalogNode[]; level: number }>()
    if (level < keys.length) for (const node of nodes) {
      const groupLevel = (level === 1 && node.database === '_ServerLevel') || (level === 2 && !node.schema?.trim()) ? 3 : level
      const name = node[keys[groupLevel]] ?? '(unknown)'
      const key = groupLevel + ':' + name
      if (!result.has(key)) result.set(key, { name, nodes: [], level: groupLevel })
      result.get(key)!.nodes.push(node)
    }
    return [...result].sort(([, a], [, b]) => b.level - a.level || a.name.localeCompare(b.name))
  }, [nodes, level])
  const sorted = useMemo(() => nodes.filter(() => level === keys.length).sort((a, b) => a.qualifiedName.localeCompare(b.qualifiedName)), [nodes, level])
  const total = sorted.length + groups.length
  return <ul className="catalog-branches">
    {groups.slice(0, limit).map(([key, group]) => <Branch key={key} name={group.name} nodes={group.nodes} level={group.level} linkEngines={linkEngines} />)}
    {sorted.slice(0, Math.max(0, limit - groups.length)).map(node => <li key={node.id}>
      <NavLink to={`/object/${node.id}`} title={node.qualifiedName}><span>{node.qualifiedName}{linkEngines.has(node.id) && <span className="catalog-engine"> ({linkEngines.get(node.id)})</span>}</span></NavLink>
    </li>)}
    {total > limit && <li><Button onClick={() => setLimit(n => n + BRANCH_CAP)}>Show more ({total - limit})</Button></li>}
  </ul>
}

function Branch({ name, nodes, level, linkEngines }: { name: string; nodes: CatalogNode[]; level: number; linkEngines: Map<string, string> }) {
  const [open, setOpen] = useState(false)
  const engines = level === 0 ? [...new Set(nodes.flatMap(node => {
    const engine = engineLabel(node.engine)
    return engine ? [engine] : []
  }))].sort() : []
  return <li><button className="catalog-branch" type="button" aria-expanded={open} onClick={() => setOpen(v => !v)}>
    <span aria-hidden="true">{open ? '▾' : '▸'}</span><span>{name}{engines.length > 0 && <span className="catalog-engine"> ({engines.join(', ')})</span>}</span><small>{nodes.length}</small>
  </button>{open && <Branches nodes={nodes} level={level + 1} linkEngines={linkEngines} />}</li>
}

export default function CatalogSidebar({ nodes, linkedServerReferences = NO_LINK_REFERENCES }: { nodes: CatalogNode[]; linkedServerReferences?: CatalogLinkedServerReference[] }) {
  const linkEngines = useMemo(() => {
    const byId = new Map(nodes.map(node => [node.id, node]))
    const serverEngines = new Map<string, Set<string>>()
    for (const node of nodes) {
      const engine = engineLabel(node.engine)
      if (!engine) continue
      const key = node.server.toLowerCase()
      if (!serverEngines.has(key)) serverEngines.set(key, new Set())
      serverEngines.get(key)!.add(engine)
    }
    const targets = new Map<string, Set<string>>()
    for (const ref of linkedServerReferences) {
      const target = ref.to ? byId.get(ref.to) : undefined
      if (!target) continue
      if (!targets.has(ref.linkedServer)) targets.set(ref.linkedServer, new Set())
      for (const engine of serverEngines.get(target.server.toLowerCase()) ?? []) targets.get(ref.linkedServer)!.add(engine)
    }
    const result = new Map<string, string>()
    for (const node of nodes.filter(isLinkNode)) {
      // A link's own engine belongs to its host, not its destination.
      const engines = targets.get(node.id) ?? serverEngines.get(node.name.toLowerCase())
      if (engines?.size) result.set(node.id, [...engines].sort().join(', '))
    }
    return result
  }, [nodes, linkedServerReferences])
  return <aside className="catalog-sidebar" aria-label="Catalog">
    <h2>Catalog</h2>
    <p className="muted">Browse objects by server, database, schema and type.</p>
    <nav aria-label="Catalog objects"><Branches nodes={nodes} linkEngines={linkEngines} /></nav>
    {nodes.length === 0 && <p className="muted">No objects in this snapshot.</p>}
  </aside>
}
