import { Link } from 'react-router-dom'
import { useCatalog } from '../lib/CatalogContext'
import TypeBadge from '../components/TypeBadge'
import { getCoChangePairs, getMostChanged, getRecentlyChanged, getTopReferencedTables, intensity } from '../lib/analytics'
import { detectMetricAnomalies } from '../lib/anomalies'
import { colorForType } from '../lib/typeColors'

export default function Home() {
  const { index } = useCatalog()
  if (!index) return null

  const { catalog } = index
  const totalObjects = catalog.nodes.length
  const totalServers = catalog.servers.length
  const totalDatabases = new Set(catalog.nodes.map((n) => `${n.server}::${n.database}`)).size

  const recentlyChanged = getRecentlyChanged(index, 10)
  const topReferenced = getTopReferencedTables(index, 10)
  const mostChanged = getMostChanged(index, 10)
  const coChanges = getCoChangePairs(index, 10)
  const maxChangeCount = mostChanged[0]?.value ?? 0
  const orphanedReferences = catalog.orphanedReferences ?? []
  const metricAnomalies = detectMetricAnomalies(catalog.nodes, 10)

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

      <div className="overview-grid">
        <section className="overview-panel">
          <h2>Latest changes</h2>
          {recentlyChanged.length === 0 ? (
            <p className="muted">No change history mined for this run (analyze-catalog ran without -RepoRoot).</p>
          ) : (
            <ol className="ranked-list">
              {recentlyChanged.map(({ node }) => (
                <li key={node.id}>
                  <Link to={`/object/${node.id}`}>{node.qualifiedName}</Link>
                  <TypeBadge type={node.type} />
                  <span className="ranked-list-meta">{new Date(node.lastChangedAt!).toLocaleDateString()}</span>
                </li>
              ))}
            </ol>
          )}
        </section>

        <section className="overview-panel">
          <h2>Most referenced tables</h2>
          <p className="muted overview-panel-hint">Direct = objects pointing straight at it. Indirect includes transitive dependents (one hop across a linked server).</p>
          {topReferenced.length === 0 ? (
            <p className="muted">No inferred references yet.</p>
          ) : (
            <ol className="ranked-list">
              {topReferenced.map(({ node, directUsers, indirectUsers }) => (
                <li key={node.id}>
                  <Link to={`/object/${node.id}`}>{node.qualifiedName}</Link>
                  <span className="ranked-list-meta">
                    {directUsers} direct &middot; {indirectUsers} indirect
                  </span>
                </li>
              ))}
            </ol>
          )}
        </section>

        <section className="overview-panel">
          <h2>Change heatmap</h2>
          <p className="muted overview-panel-hint">Objects changed most often across the mined commit history.</p>
          {mostChanged.length === 0 ? (
            <p className="muted">No change history mined for this run.</p>
          ) : (
            <ol className="heatmap-list">
              {mostChanged.map(({ node, value }) => (
                <li key={node.id}>
                  <span
                    className="heatmap-swatch"
                    style={{ background: `color-mix(in srgb, ${colorForType(node.type)} ${Math.round(intensity(value, maxChangeCount) * 100)}%, transparent)` }}
                  />
                  <Link to={`/object/${node.id}`}>{node.qualifiedName}</Link>
                  <span className="ranked-list-meta">{value} change{value === 1 ? '' : 's'}</span>
                </li>
              ))}
            </ol>
          )}
        </section>

        <section className="overview-panel">
          <h2>Commonly changed together</h2>
          <p className="muted overview-panel-hint">Objects that keep showing up in the same commit.</p>
          {coChanges.length === 0 ? (
            <p className="muted">No co-change pairs found yet.</p>
          ) : (
            <ol className="ranked-list">
              {coChanges.map((pair) => (
                <li key={`${pair.a}|${pair.b}`}>
                  {pair.nodeA ? <Link to={`/object/${pair.a}`}>{pair.nodeA.qualifiedName}</Link> : pair.a}
                  {' ↔ '}
                  {pair.nodeB ? <Link to={`/object/${pair.b}`}>{pair.nodeB.qualifiedName}</Link> : pair.b}
                  <span className="ranked-list-meta">{pair.count}&times;</span>
                </li>
              ))}
            </ol>
          )}
        </section>

        <section className="overview-panel">
          <h2>Metrics anomalies</h2>
          <p className="muted overview-panel-hint">
            Tables whose latest metrics snapshot swung sharply versus the previous one - a row-count jump/drop or an
            index fragmentation spike - flagged for a look, not a certified alert.
          </p>
          {metricAnomalies.length === 0 ? (
            <p className="muted">No anomalies in the latest metrics snapshots.</p>
          ) : (
            <ol className="ranked-list">
              {metricAnomalies.map((a, i) => (
                <li key={`${a.node.id}|${a.kind}|${i}`}>
                  <Link to={`/object/${a.node.id}`}>{a.node.qualifiedName}</Link>
                  <span className="ranked-list-meta ranked-list-meta--wrap">{a.message}</span>
                </li>
              ))}
            </ol>
          )}
        </section>

        <section className="overview-panel">
          <h2>Orphaned references</h2>
          <p className="muted overview-panel-hint">
            References that don&apos;t resolve to anything in the catalog&apos;s scope - usually a renamed or dropped
            target the caller was never updated for.
          </p>
          {orphanedReferences.length === 0 ? (
            <p className="muted">No orphaned references detected.</p>
          ) : (
            <ol className="ranked-list">
              {orphanedReferences.slice(0, 10).map((ref, i) => {
                const fromNode = index.byId.get(ref.from)
                const target = ref.schema ? `${ref.schema}.${ref.name}` : ref.name
                return (
                  <li key={`${ref.from}|${ref.schema ?? ''}|${ref.name}|${i}`}>
                    {fromNode ? <Link to={`/object/${fromNode.id}`}>{fromNode.qualifiedName}</Link> : ref.from}
                    <span className="ranked-list-meta">&rarr; {target}</span>
                  </li>
                )
              })}
            </ol>
          )}
          {orphanedReferences.length > 10 && (
            <p className="muted overview-panel-hint">
              +{orphanedReferences.length - 10} more not shown.
            </p>
          )}
        </section>
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
