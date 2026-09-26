import { expect, it } from 'vitest'
import { detectMetricAnomalies } from './anomalies'
import { makeNode } from '../test/fixtures'
import type { CatalogMetricSnapshot } from '../types'
const metric = (rowCount: number | null, indexes: CatalogMetricSnapshot['indexes'] = []): CatalogMetricSnapshot => ({
  capturedAt: '2026-01-01',
  rowCount,
  indexes,
  statistics: [],
  reservedKB: null,
  dataKB: null,
  indexKB: null,
})
it('detects significant row swings and fragmentation spikes, ranks severity, and enforces the limit', () => {
  const nodes = [
    makeNode({ id: 'jump', metrics: [metric(1000), metric(3000)] }),
    makeNode({ id: 'drop', metrics: [metric(1000), metric(0)] }),
    makeNode({
      id: 'fragments',
      metrics: [
        metric(null, [
          { name: 'known', fragmentationPct: 10 },
          { name: 'null', fragmentationPct: null },
          { name: 'stable', fragmentationPct: 40 },
        ]),
        metric(null, [
          { name: 'known', fragmentationPct: 40 },
          { name: 'null', fragmentationPct: 30 },
          { name: 'new', fragmentationPct: 50 },
          { name: 'stable', fragmentationPct: 45 },
          { name: 'low', fragmentationPct: 10 },
          { name: 'absent' },
          { name: 'none', fragmentationPct: null },
        ]),
      ],
    }),
  ]
  const result = detectMetricAnomalies(nodes)
  expect(result.map((item) => item.kind)).toEqual([
    'row-count-jump',
    'row-count-drop',
    'fragmentation-spike',
    'fragmentation-spike',
    'fragmentation-spike',
  ])
  expect(result[2].message).toBe('Index "new" fragmentation at 50%')
  expect(result[3].message).toBe('Index "known" fragmentation jumped to 40% (from 10%)')
  expect(detectMetricAnomalies(nodes, 1)).toEqual([result[0]])
})
it('ignores insufficient, unavailable, zero-baseline, small and low-percentage changes', () => {
  expect(
    detectMetricAnomalies([
      makeNode({ id: 'empty' }),
      ...[
        [null, 1000],
        [1000, null],
        [0, 1000],
        [100, 200],
        [10000, 10600],
      ].map(([a, b], i) => makeNode({ id: String(i), metrics: [metric(a), metric(b)] })),
    ]),
  ).toEqual([])
})
