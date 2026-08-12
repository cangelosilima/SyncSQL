import { Link } from 'react-router-dom'
import { useCatalog } from '../lib/CatalogContext'
import TypeBadge from '../components/TypeBadge'

export default function Home() {
  const { index } = useCatalog()
  if (!index) return null

  const { catalog } = index
  const totalObjects = catalog.nodes.length
  const totalServers = catalog.servers.length
  const totalDatabases = new Set(catalog.nodes.map((n) => `${n.server}::${n.database}`)).size

  return (
    <div className="page">
      <h1>SyncSQL Catalog</h1>
      <p className="muted">
        Generated {new Date(catalog.generatedAt).toLocaleString()} from {totalServers} server(s), {totalDatabases}{' '}
        database(s), {totalObjects} object(s).
      </p>

      <div className="stat-cards">
        {Object.entries(catalog.typeCounts)
          .sort(([, a], [, b]) => b - a)
          .map(([type, count]) => (
            <div key={type} className="stat-card">
              <TypeBadge type={type} />
              <div className="stat-card-count">{count}</div>
            </div>
          ))}
      </div>

      <h2>Servers</h2>
      <ul className="server-list">
        {index.tree.map((server) => (
          <li key={server.name}>
            <strong>{server.name}</strong>
            <ul>
              {server.databases.map((db) => (
                <li key={db.name}>{db.name}</li>
              ))}
            </ul>
          </li>
        ))}
      </ul>

      <p className="disclaimer">
        Data lineage on the <Link to="/lineage">Lineage</Link> page is inferred by regex-matching object names inside
        each object&apos;s DDL text - not a real SQL parser. Treat it as a starting point for exploration, not a
        certified lineage report: it can miss dynamic SQL and cross-linked-server references, and can occasionally
        produce a false-positive edge when an identifier collides with an unrelated object name.
      </p>
    </div>
  )
}
