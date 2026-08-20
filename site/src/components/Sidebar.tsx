import { useMemo, useState } from 'react'
import { NavLink } from 'react-router-dom'
import { useCatalog } from '../lib/CatalogContext'
import { matchesQuery } from '../lib/catalog'
import type { CatalogNode } from '../types'
import TypeBadge from './TypeBadge'

interface SidebarProps {
  onNavigate?: () => void
}

export default function Sidebar({ onNavigate }: SidebarProps) {
  const { index } = useCatalog()
  const [query, setQuery] = useState('')

  const searching = query.trim().length > 0

  const filteredTree = useMemo(() => {
    if (!index) return []
    if (!searching) return index.tree

    const q = query.trim()
    return index.tree
      .map((server) => ({
        ...server,
        databases: server.databases
          .map((db) => ({
            ...db,
            schemas: db.schemas
              .map((schema) => ({
                ...schema,
                types: schema.types
                  .map((type) => ({ ...type, nodes: type.nodes.filter((n) => matchesQuery(n, q)) }))
                  .filter((type) => type.nodes.length > 0),
              }))
              .filter((schema) => schema.types.length > 0),
          }))
          .filter((db) => db.schemas.length > 0),
      }))
      .filter((server) => server.databases.length > 0)
  }, [index, query, searching])

  if (!index) return null

  return (
    <nav className="sidebar">
      <div className="sidebar-search">
        <input
          type="search"
          placeholder="Search objects..."
          value={query}
          onChange={(e) => setQuery(e.target.value)}
          aria-label="Search catalog objects"
        />
      </div>
      <div className="sidebar-tree">
        {filteredTree.map((server) => (
          <details key={server.name} open={searching}>
            <summary className="tree-server">{server.name}</summary>
            {server.databases.map((db) => (
              <details key={db.name} open={searching}>
                <summary className="tree-database">{db.name}</summary>
                {db.schemas.map((schema) => (
                  <details key={schema.name} open={searching}>
                    <summary className="tree-schema">{schema.name}</summary>
                    {schema.types.map((type) => (
                      <details key={type.name} open={searching}>
                        <summary className="tree-type">
                          <TypeBadge type={type.name} />
                        </summary>
                        {type.nodes.map((node) => (
                          <ObjectLink key={node.id} node={node} onNavigate={onNavigate} />
                        ))}
                      </details>
                    ))}
                  </details>
                ))}
              </details>
            ))}
          </details>
        ))}
        {filteredTree.length === 0 && <p className="sidebar-empty">No objects match &quot;{query}&quot;.</p>}
      </div>
    </nav>
  )
}

function ObjectLink({ node, onNavigate }: { node: CatalogNode; onNavigate?: () => void }) {
  return (
    <NavLink to={`/object/${node.id}`} className="tree-object" onClick={onNavigate}>
      {node.name}
    </NavLink>
  )
}
