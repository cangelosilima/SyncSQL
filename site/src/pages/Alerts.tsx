import { useMemo, useState, useEffect } from 'react'
import { Link, useSearchParams } from 'react-router-dom'
import { useCatalog } from '../lib/CatalogContext'
import { detectMetricAnomalies } from '../lib/anomalies'
import { qualifiedRefName } from '../lib/catalog'
import HelpButton from '../components/HelpButton'

export default function Alerts() {
  const { index } = useCatalog()
  const [params, setParams] = useSearchParams()
  const category = params.get('category') ?? 'all'
  const query = params.get('q') ?? ''
  const [limit, setLimit] = useState(100)
  useEffect(() => setLimit(100), [category, query])
  const alerts = useMemo(() => {
    if (!index) return []
    return [
      ...detectMetricAnomalies(index.catalog.nodes, Infinity).map((alert, i) => ({
        key: `metric-${i}`,
        category: 'metrics',
        label: 'Metric anomaly',
        evidence: 'Heuristic',
        node: alert.node,
        objectId: alert.node.id,
        message: alert.message,
      })),
      ...(index.catalog.orphanedReferences ?? []).map((ref, i) => ({
        key: `orphan-${i}`,
        category: 'orphans',
        label: 'Orphaned reference',
        evidence: 'Catalog finding',
        node: index.byId.get(ref.from),
        objectId: ref.from,
        message: `Unresolved reference to ${qualifiedRefName(ref)}`,
      })),
    ]
  }, [index])
  const matches = useMemo(
    () =>
      alerts.filter(
        (alert) =>
          (category === 'all' || category === alert.category) &&
          [
            alert.node?.qualifiedName,
            alert.node?.server,
            alert.node?.database,
            alert.objectId,
            alert.message,
            alert.label,
          ]
            .join(' ')
            .toLowerCase()
            .includes(query.trim().toLowerCase()),
      ),
    [alerts, category, query],
  )
  if (!index) return null
  function update(key: string, value: string) {
    const next = new URLSearchParams(params)
    if (value && value !== 'all') next.set(key, value)
    else next.delete(key)
    setParams(next, { replace: true })
  }
  const metrics = alerts.filter((alert) => alert.category === 'metrics').length
  return (
    <div className="page alerts-page">
      <h1 className="page-title">
        Alerts <HelpButton topic="alerts" />
      </h1>
      <p className="muted">
        Investigate metric anomalies and unresolved references in the catalog snapshot from{' '}
        {new Date(index.catalog.generatedAt).toLocaleString()}.
      </p>
      <p>
        {metrics} metric anomalies · {alerts.length - metrics} orphaned references
      </p>
      <div className="alerts-controls">
        <label>
          Category{' '}
          <select value={category} onChange={(event) => update('category', event.target.value)}>
            <option value="all">All alerts</option>
            <option value="metrics">Metric anomalies</option>
            <option value="orphans">Orphaned references</option>
          </select>
        </label>
        <label>
          Search alerts{' '}
          <input
            type="search"
            value={query}
            onChange={(event) => update('q', event.target.value)}
            placeholder="Object, server, database or evidence"
          />
        </label>
      </div>
      <details className="alerts-explanation">
        <summary>How these alerts are detected</summary>
        <p>
          Metric anomalies are heuristic signals comparing the latest two metric snapshots. Row-count changes require at
          least 50% and 500 rows. Index fragmentation must reach at least 30%, with an increase of at least 20
          percentage points; a missing previous index reading uses the current fragmentation value. These signals
          warrant investigation, not an automatic conclusion of failure.
        </p>
        <p>
          Orphaned references do not resolve within the lookup scope: the object's database, the rest of its server, or
          a server one linked server away. References into systems that are not extracted are excluded. A renamed or
          dropped target is a possible cause, not a confirmed diagnosis.
        </p>
        <p>
          Objects need at least two metric snapshots for anomaly evaluation. Missing metrics or reference analysis do
          not establish that an object is healthy. Alerts describe this published snapshot; they are not live
          monitoring.
        </p>
      </details>
      {index.catalog.orphanedReferences === undefined && (
        <p className="muted">Orphaned-reference analysis is not included in this snapshot.</p>
      )}
      <p role="status">
        {matches.length} of {alerts.length} alerts match{matches.length > limit ? ` · showing ${limit}` : ''}
      </p>
      {matches.length === 0 ? (
        <p className="empty-state">
          {alerts.length ? 'No alerts match these filters.' : 'No alerts detected from the available catalog evidence.'}
        </p>
      ) : (
        <div className="alerts-table-wrap">
          <table className="columns-table">
            <thead>
              <tr>
                <th scope="col">Category</th>
                <th scope="col">Object</th>
                <th scope="col">Evidence</th>
                <th scope="col">Investigate</th>
              </tr>
            </thead>
            <tbody>
              {matches.slice(0, limit).map((alert) => (
                <tr key={alert.key}>
                  <td>
                    {alert.label}
                    <div className="muted">{alert.evidence}</div>
                  </td>
                  <td>
                    {alert.node ? (
                      <Link to={`/object/${alert.objectId}`}>{alert.node.qualifiedName}</Link>
                    ) : (
                      alert.objectId
                    )}
                    <div className="muted">
                      {alert.node && `${alert.node.server} / ${alert.node.database} · ${alert.node.type}`}
                    </div>
                  </td>
                  <td>{alert.message}</td>
                  <td>
                    {alert.node ? (
                      <div className="alerts-links">
                        <Link to={`/object/${alert.objectId}`}>Inspect object</Link>
                        <Link to={`/lineage?focus=${encodeURIComponent(alert.objectId)}`}>Explore lineage</Link>
                      </div>
                    ) : (
                      <span className="muted">Source object unavailable</span>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
      {matches.length > limit && (
        <button type="button" className="ui-button" onClick={() => setLimit((value) => value + 100)}>
          Show more ({matches.length - limit})
        </button>
      )}
    </div>
  )
}
