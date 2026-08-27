import type { CatalogNode } from '../types'

export type MetricAnomalyKind = 'row-count-jump' | 'row-count-drop' | 'fragmentation-spike'

export interface MetricAnomaly {
  node: CatalogNode
  kind: MetricAnomalyKind
  message: string
  /** 0-1+ severity score used only for ranking, not displayed. */
  magnitude: number
}

const ROW_COUNT_PCT_THRESHOLD = 0.5
const ROW_COUNT_MIN_ABS_DELTA = 500
const FRAGMENTATION_PCT_FLOOR = 30
const FRAGMENTATION_JUMP_THRESHOLD = 20

/**
 * Flags tables whose latest metrics snapshot looks like a real operational
 * anomaly versus the previous one - a sudden row-count swing, or an index
 * fragmentation spike. Heuristic thresholds, not a statistical model: this
 * is meant to surface things worth a look on the Overview page, not to be a
 * certified alerting signal. Requires at least two snapshots (nothing to
 * compare a single reading against).
 */
export function detectMetricAnomalies(nodes: CatalogNode[], limit = 10): MetricAnomaly[] {
  const anomalies: MetricAnomaly[] = []

  for (const node of nodes) {
    const metrics = node.metrics
    if (metrics.length < 2) continue
    const latest = metrics[metrics.length - 1]
    const prev = metrics[metrics.length - 2]

    if (latest.rowCount !== null && prev.rowCount !== null && prev.rowCount > 0) {
      const delta = latest.rowCount - prev.rowCount
      const pct = Math.abs(delta) / prev.rowCount
      if (pct >= ROW_COUNT_PCT_THRESHOLD && Math.abs(delta) >= ROW_COUNT_MIN_ABS_DELTA) {
        anomalies.push({
          node,
          kind: delta > 0 ? 'row-count-jump' : 'row-count-drop',
          message: `Row count ${delta > 0 ? 'jumped' : 'dropped'} ${Math.round(pct * 100)}% since the previous snapshot (${prev.rowCount.toLocaleString()} → ${latest.rowCount.toLocaleString()})`,
          magnitude: pct,
        })
      }
    }

    for (const idx of latest.indexes) {
      if (idx.fragmentationPct === null || idx.fragmentationPct === undefined) continue
      if (idx.fragmentationPct < FRAGMENTATION_PCT_FLOOR) continue
      const prevFrag = prev.indexes.find((i) => i.name === idx.name)?.fragmentationPct
      const jump = prevFrag === null || prevFrag === undefined ? idx.fragmentationPct : idx.fragmentationPct - prevFrag
      if (jump >= FRAGMENTATION_JUMP_THRESHOLD) {
        anomalies.push({
          node,
          kind: 'fragmentation-spike',
          message:
            prevFrag === null || prevFrag === undefined
              ? `Index "${idx.name}" fragmentation at ${idx.fragmentationPct.toFixed(0)}%`
              : `Index "${idx.name}" fragmentation jumped to ${idx.fragmentationPct.toFixed(0)}% (from ${prevFrag.toFixed(0)}%)`,
          magnitude: idx.fragmentationPct / 100,
        })
      }
    }
  }

  return anomalies.sort((a, b) => b.magnitude - a.magnitude).slice(0, limit)
}
