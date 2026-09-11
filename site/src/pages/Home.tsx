import { Link } from 'react-router-dom'
import { useCatalog } from '../lib/CatalogContext'
import TypeBadge from '../components/TypeBadge'
import HelpButton from '../components/HelpButton'
import TypeActivityHeatmap, { CHANGE_ACTIVITY_WEEKS } from '../components/TypeActivityHeatmap'
import {
  formatRelative,
  getCoChangePairs,
  getMostChanged,
  getRecentlyChanged,
  getTopReferencedTables,
  intensity,
} from '../lib/analytics'
import { detectMetricAnomalies } from '../lib/anomalies'
import { colorForType } from '../lib/typeColors'
import BenchmarkExample from '../components/BenchmarkExample'

export default function Home() {
  const { index } = useCatalog()
  if (!index) return null

  const { catalog } = index
  const totalObjects = catalog.nodes.length
  const totalServers = catalog.servers.length
  const totalDatabases = new Set(
    catalog.nodes.filter((n) => n.database !== '_ServerLevel').map((n) => `${n.server}::${n.database}`),
  ).size

  const recentlyChanged = getRecentlyChanged(index, 10)
  const topReferenced = getTopReferencedTables(index, 10)
  const mostChanged = getMostChanged(index, 10)
  const coChanges = getCoChangePairs(index, 10)
  const maxChangeCount = mostChanged[0]?.value ?? 0
  const orphanedReferences = catalog.orphanedReferences ?? []
  const metricAnomalies = detectMetricAnomalies(catalog.nodes, Infinity)
  const lastChangedNode = recentlyChanged[0]?.node
  const typeCounts = Object.entries(catalog.typeCounts).sort(([, a], [, b]) => b - a)
  const typeOrder = typeCounts.map(([type]) => type)
  const nodeTypeById = new Map(catalog.nodes.map((n) => [n.id, n.type]))

  return (
    <div className="page page--wide overview-page">
      <h1 className="page-title">
        Overview
        <HelpButton topic="overview" />
      </h1>
      <div className="sync-line">
        <span className="sync-line-badge">Catalog snapshot</span>
        <span>
          Last sync: {new Date(catalog.generatedAt).toLocaleString()} &middot; {totalServers} server(s) &middot;{' '}
          {totalDatabases} database(s)
        </span>
      </div>

      <BenchmarkExample catalog={catalog} />

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
          <div className="quick-stat-label">
            <Link to="/alerts">Alerts</Link>
          </div>
          <div className="quick-stat-value">{metricAnomalies.length + orphanedReferences.length}</div>
          <div className="quick-stat-sub">
            {metricAnomalies.length} anomalies · {orphanedReferences.length} orphaned references
          </div>
        </div>
        <div className="quick-stat">
          <div className="quick-stat-label">Last change</div>
          <div className="quick-stat-value">
            {lastChangedNode ? formatRelative(lastChangedNode.lastChangedAt!) : '-'}
          </div>
          <div className="quick-stat-sub">{lastChangedNode ? lastChangedNode.qualifiedName : 'no history mined'}</div>
        </div>
      </div>

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
          <p className="muted overview-panel-hint">
            Direct = objects pointing straight at it. Indirect includes direct and transitive dependents; traversal
            stops after crossing a server and at six hops.
          </p>
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
                    style={{
                      background: `color-mix(in srgb, ${colorForType(node.type)} ${Math.round(intensity(value, maxChangeCount) * 100)}%, transparent)`,
                    }}
                  />
                  <Link to={`/object/${node.id}`}>{node.qualifiedName}</Link>
                  <span className="ranked-list-meta">
                    {value} change{value === 1 ? '' : 's'}
                  </span>
                </li>
              ))}
            </ol>
          )}
        </section>

        <section className="overview-panel">
          <h2>Commonly changed together</h2>
          <p className="muted overview-panel-hint">
            Objects that keep showing up in the same commit. Co-occurrence is not a dependency or a causal relationship.
          </p>
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
