import { useMemo, useState } from 'react'
import { NavLink } from 'react-router-dom'
import { useCatalog } from '../lib/CatalogContext'
import { matchesQuery } from '../lib/catalog'
import type { CatalogNode } from '../types'
import TypeBadge from './TypeBadge'

export default function Sidebar() {
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
            types: db.types
              .map((type) => ({
                ...type,
                schemas: type.schemas
                  .map((schema) => ({ ...schema, nodes: schema.nodes.filter((n) => matchesQuery(n, q)) }))
                  .filter((schema) => schema.nodes.length > 0),
                looseNodes: type.looseNodes.filter((n) => matchesQuery(n, q)),
              }))
              .filter((type) => type.schemas.length > 0 || type.looseNodes.length > 0),
          }))
          .filter((db) => db.types.length > 0),
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
                {db.types.map((type) => (
                  <details key={type.name} open={searching}>
                    <summary className="tree-type">
                      <TypeBadge type={type.name} />
                    </summary>
                    {type.looseNodes.map((node) => (
                      <ObjectLink key={node.id} node={node} />
                    ))}
                    {type.schemas.map((schema) => (
                      <details key={schema.name} open={searching}>
                        <summary className="tree-schema">{schema.name}</summary>
                        {schema.nodes.map((node) => (
                          <ObjectLink key={node.id} node={node} />
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

function ObjectLink({ node }: { node: CatalogNode }) {
  return (
    <NavLink to={`/object/${node.id}`} className="tree-object">
      {node.name}
    </NavLink>
  )
}
