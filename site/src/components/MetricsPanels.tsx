import { useMemo, useState } from 'react'
import TrendChart from './TrendChart'
import type { CatalogMetricSnapshot } from '../types'

function formatKB(kb: number | null): string {
  if (kb === null || kb === undefined) return '-'
  if (kb >= 1024 * 1024) return `${(kb / (1024 * 1024)).toFixed(1)} GB`
  if (kb >= 1024) return `${(kb / 1024).toFixed(1)} MB`
  return `${kb} KB`
}

function formatCount(n: number | null): string {
  if (n === null || n === undefined) return '-'
  return n.toLocaleString()
}

/**
 * Volume/index/optimizer-statistics trend graphs for a table, driven by
 * node.metrics - a growing, non-versioned history of volatile operational
 * snapshots (see README's "Volatile metrics" section). Renders nothing
 * useful and should be gated by the caller when metrics.length === 0.
 */
export default function MetricsPanels({ metrics }: { metrics: CatalogMetricSnapshot[] }) {
  const labels = useMemo(() => metrics.map((m) => new Date(m.capturedAt).toLocaleDateString()), [metrics])
  const latest = metrics[metrics.length - 1]

  const indexNames = useMemo(() => {
    const names = new Set<string>()
    for (const snap of metrics) for (const idx of snap.indexes) names.add(idx.name)
    return [...names]
  }, [metrics])
  const [selectedIndex, setSelectedIndex] = useState(indexNames[0] ?? '')
  const activeIndexName = indexNames.includes(selectedIndex) ? selectedIndex : indexNames[0]

  const indexSeriesFor = (name: string) => metrics.map((snap) => snap.indexes.find((i) => i.name === name) ?? null)
  const activeIndexSnaps = activeIndexName ? indexSeriesFor(activeIndexName) : []
  const isMssqlIndex = activeIndexSnaps.some((s) => s && s.fragmentationPct !== undefined && s.fragmentationPct !== null)
  const isOracleIndex = activeIndexSnaps.some((s) => s && s.rowCount !== undefined && s.rowCount !== null)

  const totalPendingMods = metrics.map((snap) =>
    snap.statistics.length === 0
      ? null
      : snap.statistics.reduce((sum, s) => sum + (s.modificationCounter ?? 0), 0),
  )

  return (
    <div className="metrics-grid">
      <div className="metrics-panel">
        <h3>Volume</h3>
        <div className="metrics-stat-row">
          <div>
            <strong>{formatCount(latest?.rowCount ?? null)}</strong>
            rows
          </div>
          <div>
            <strong>{formatKB(latest?.reservedKB ?? null)}</strong>
            reserved
          </div>
        </div>
        <TrendChart
          labels={labels}
          series={[{ name: 'Row count', color: '#3b82f6', values: metrics.map((m) => m.rowCount) }]}
          formatValue={formatCount}
        />
      </div>

      <div className="metrics-panel">
        <h3>Size</h3>
        <TrendChart
          labels={labels}
          series={[
            { name: 'Reserved', color: '#64748b', values: metrics.map((m) => m.reservedKB) },
            { name: 'Data', color: '#3b82f6', values: metrics.map((m) => m.dataKB) },
            { name: 'Index', color: '#f59e0b', values: metrics.map((m) => m.indexKB) },
          ]}
          formatValue={formatKB}
        />
      </div>

      {indexNames.length > 0 && (
        <div className="metrics-panel">
          <h3>Indexes</h3>
          <div className="metrics-index-tabs">
            {indexNames.map((name) => (
              <button
                key={name}
                type="button"
                className={name === activeIndexName ? 'metrics-index-tab active' : 'metrics-index-tab'}
                onClick={() => setSelectedIndex(name)}
              >
                {name}
              </button>
            ))}
          </div>
          {isMssqlIndex && (
            <>
              <TrendChart
                labels={labels}
                series={[{ name: 'Fragmentation %', color: '#ef4444', values: activeIndexSnaps.map((s) => s?.fragmentationPct ?? null) }]}
                formatValue={(v) => `${v.toFixed(1)}%`}
              />
              <TrendChart
                labels={labels}
                series={[
                  { name: 'Seeks', color: '#10b981', values: activeIndexSnaps.map((s) => s?.seeks ?? null) },
                  { name: 'Scans', color: '#f59e0b', values: activeIndexSnaps.map((s) => s?.scans ?? null) },
                  { name: 'Lookups', color: '#8b5cf6', values: activeIndexSnaps.map((s) => s?.lookups ?? null) },
                  { name: 'Updates', color: '#ef4444', values: activeIndexSnaps.map((s) => s?.updates ?? null) },
                ]}
                formatValue={formatCount}
              />
            </>
          )}
          {!isMssqlIndex && isOracleIndex && (
            <TrendChart
              labels={labels}
              series={[
                { name: 'Rows', color: '#3b82f6', values: activeIndexSnaps.map((s) => s?.rowCount ?? null) },
                { name: 'Leaf blocks', color: '#8b5cf6', values: activeIndexSnaps.map((s) => s?.leafBlocks ?? null) },
              ]}
              formatValue={formatCount}
            />
          )}
        </div>
      )}

      {latest && latest.statistics.length > 0 && (
        <div className="metrics-panel">
          <h3>Optimizer statistics</h3>
          <p className="muted overview-panel-hint" style={{ marginTop: 0 }}>
            What the query optimizer uses for cardinality estimation - modification counter tracks rows changed since
            the last time each statistics object was refreshed.
          </p>
          <TrendChart
            labels={labels}
            series={[{ name: 'Pending modifications (all stats)', color: '#ef4444', values: totalPendingMods }]}
            formatValue={formatCount}
          />
          <table className="columns-table">
            <thead>
              <tr>
                <th>Name</th>
                <th>Rows</th>
                <th>Sampled</th>
                <th>Steps</th>
                <th>Modified</th>
                <th>Last updated</th>
              </tr>
            </thead>
            <tbody>
              {latest.statistics.map((s) => (
                <tr key={s.name}>
                  <td>{s.name}</td>
                  <td>{formatCount(s.rows)}</td>
                  <td>{formatCount(s.rowsSampled)}</td>
                  <td>{s.steps ?? <span className="muted">-</span>}</td>
                  <td>{formatCount(s.modificationCounter)}</td>
                  <td>{s.lastUpdated ? new Date(s.lastUpdated).toLocaleString() : <span className="muted">-</span>}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  )
}
