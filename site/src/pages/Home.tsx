import { Link } from 'react-router-dom'
import { useCatalog } from '../lib/CatalogContext'
import TypeBadge from '../components/TypeBadge'
import TypeActivityHeatmap, { CHANGE_ACTIVITY_WEEKS } from '../components/TypeActivityHeatmap'
import { formatRelative, getCoChangePairs, getMostChanged, getRecentlyChanged, getTopReferencedTables, intensity } from '../lib/analytics'
import { detectMetricAnomalies } from '../lib/anomalies'
import { colorForType } from '../lib/typeColors'
import { qualifiedRefName } from '../lib/catalog'

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
  const lastChangedNode = recentlyChanged[0]?.node
  const typeCounts = Object.entries(catalog.typeCounts).sort(([, a], [, b]) => b - a)
  const typeOrder = typeCounts.map(([type]) => type)
  const nodeTypeById = new Map(catalog.nodes.map((n) => [n.id, n.type]))

  return (
    <div className="page">
      <h1>Overview</h1>
      <div className="sync-line">
        <span className="sync-line-badge">
          <span className="status-dot" /> Synced
        </span>
        <span>
          Last sync: {new Date(catalog.generatedAt).toLocaleString()} &middot; {totalServers} server(s) &middot;{' '}
          {totalDatabases} database(s)
        </span>
      </div>

      <div className="quick-stats">
        <div className="quick-stat">
          <div className="quick-stat-label">Total objects</div>
          <div className="quick-stat-value">{totalObjects}</div>
          <div className="quick-stat-sub">across {totalDatabases} database(s)</div>
        </div>
        <div className="quick-stat">
          <div className="quick-stat-label">Commits mined</div>
          <div className="quick-stat-value">{catalog.recentChanges.length}</div>
          <div className="quick-stat-sub">in the mined commit window</div>
        </div>
        <div className="quick-stat">
          <div className="quick-stat-label">Lineage edges</div>
          <div className="quick-stat-value">{catalog.edges.length}</div>
          <div className="quick-stat-sub">resolved references</div>
        </div>
        <div className="quick-stat">
          <div className="quick-stat-label">Last change</div>
          <div className="quick-stat-value">{lastChangedNode ? formatRelative(lastChangedNode.lastChangedAt!) : '-'}</div>
          <div className="quick-stat-sub">{lastChangedNode ? lastChangedNode.qualifiedName : 'no history mined'}</div>
        </div>
      </div>

      <h2 className="section-label">
        Metric anomalies{metricAnomalies.length > 0 ? ` - ${metricAnomalies.length} detected` : ''}
      </h2>
      {metricAnomalies.length === 0 ? (
        <p className="muted">No anomalies in the latest metrics snapshots.</p>
      ) : (
        <div className="alert-cards">
          {metricAnomalies.map((a, i) => (
            <div key={`${a.node.id}|${a.kind}|${i}`} className="alert-card">
              <span className="alert-card-icon">!</span>
              <div className="alert-card-body">
                <div className="alert-card-title">
                  <Link to={`/object/${a.node.id}`}>{a.node.qualifiedName}</Link>
                  <TypeBadge type={a.node.type} />
                </div>
                <div className="alert-card-detail">{a.message}</div>
              </div>
            </div>
          ))}
        </div>
      )}

      <h2 className="section-label">
        Orphaned references{orphanedReferences.length > 0 ? ` - ${orphanedReferences.length} detected` : ''}
      </h2>
      <p className="muted overview-panel-hint">
        References that don&apos;t resolve to anything the lookup reaches - the object&apos;s own database, the rest
        of its server, or a server one linked server away - usually a renamed or dropped target the caller was
        never updated for. A reference into something nobody extracts isn&apos;t counted here.
      </p>
      {orphanedReferences.length === 0 ? (
        <p className="muted">No orphaned references detected.</p>
      ) : (
        <div className="alert-cards">
          {orphanedReferences.slice(0, 10).map((ref, i) => {
            const fromNode = index.byId.get(ref.from)
            const target = qualifiedRefName(ref)
            return (
              <div key={`${ref.from}|${ref.server ?? ''}|${ref.database ?? ''}|${ref.schema ?? ''}|${ref.name}|${i}`} className="alert-card">
                <span className="alert-card-icon">!</span>
                <div className="alert-card-body">
                  <div className="alert-card-title">
                    {fromNode ? <Link to={`/object/${fromNode.id}`}>{fromNode.qualifiedName}</Link> : ref.from}
                  </div>
                  <div className="alert-card-detail">references {target}</div>
                </div>
              </div>
            )
          })}
          {orphanedReferences.length > 10 && (
            <p className="muted overview-panel-hint">+{orphanedReferences.length - 10} more not shown.</p>
          )}
        </div>
      )}

      <div className="overview-grid">
        <section className="overview-panel">
          <h2 className="panel-title-divided">Change activity &mdash; last {CHANGE_ACTIVITY_WEEKS} weeks</h2>
          {catalog.recentChanges.length === 0 ? (
            <p className="muted">No change history mined for this run (analyze-catalog ran without -RepoRoot).</p>
          ) : (
            <TypeActivityHeatmap commits={catalog.recentChanges} typeById={nodeTypeById} types={typeOrder} />
          )}
        </section>

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
          <h2>Most changed objects</h2>
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
      </div>
    </div>
  )
}
