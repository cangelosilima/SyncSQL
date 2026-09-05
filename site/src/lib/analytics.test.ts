import { afterEach, describe, expect, it, vi } from 'vitest'
import { makeCatalog, makeEdge, makeNode } from '../test/fixtures'
import { buildIndex } from './catalog'
import { epochOf, formatRelative, getCoChangePairs, getMostChanged, getRecentlyChanged, getTopReferencedTables, intensity } from './analytics'

afterEach(() => vi.useRealTimers())

describe('catalog analytics', () => {
  it.each([null, undefined, '', 'invalid'])('treats absent or invalid dates as zero: %s', (date) => {
    expect(epochOf(date)).toBe(0)
  })

  it('orders timestamps by instant across different UTC offsets and limits rankings', () => {
    const earlier = makeNode({ id: 'earlier', lastChangedAt: '2026-01-02T01:00:00+03:00', changeCount: 5 })
    const later = makeNode({ id: 'later', lastChangedAt: '2026-01-01T23:00:00Z', changeCount: 2 })
    const index = buildIndex(makeCatalog({ nodes: [earlier, later, makeNode({ id: 'unchanged' })] }))
    expect(getRecentlyChanged(index).map((rank) => rank.node.id)).toEqual(['later', 'earlier'])
    expect(getRecentlyChanged(index, 1)).toEqual([{ node: later, value: 0 }])
    expect(getMostChanged(index)).toEqual([{ node: earlier, value: 5 }, { node: later, value: 2 }])
    expect(getMostChanged(index, 1)).toHaveLength(1)
  })

  it('ranks tables by direct users then transitive users and ignores unused tables', () => {
    const nodes = ['a', 'b', 'c', 'unused'].map((id) => makeNode({ id }))
    nodes.push(...['view1', 'view2', 'view3', 'caller'].map((id) => makeNode({ id, type: 'Views' })))
    const index = buildIndex(makeCatalog({ nodes, edges: [makeEdge('view1', 'a'), makeEdge('view2', 'b'), makeEdge('caller', 'view2'), makeEdge('view1', 'c'), makeEdge('view3', 'c')] }))
    expect(getTopReferencedTables(index).map(({ node, directUsers, indirectUsers }) => [node.id, directUsers, indirectUsers])).toEqual([
      ['c', 2, 2], ['b', 1, 2], ['a', 1, 1],
    ])
    expect(getTopReferencedTables(index, 1)[0].node.id).toBe('c')
  })

  it('resolves co-change endpoints while preserving missing historical objects', () => {
    const node = makeNode({ id: 'a' })
    const pair = { a: 'a', b: 'deleted', count: 2 }
    const index = buildIndex(makeCatalog({ nodes: [node], coChangePairs: [pair] }))
    expect(getCoChangePairs(index)).toEqual([{ ...pair, nodeA: node, nodeB: undefined }])
    expect(getCoChangePairs(index, 0)).toEqual([])
  })

  it.each([[5, 0, 0], [5, -1, 0], [0, 10, 0.08], [5, 10, 0.5], [20, 10, 1]])('clamps intensity(%s, %s)', (value, max, expected) => {
    expect(intensity(value, max)).toBe(expected)
  })

  it.each([[0, 'just now'], [5, '5m ago'], [120, '2h ago'], [2880, '2d ago'], [86400, '2mo ago'], [1051200, '2y ago']])('formats %s minutes elapsed', (minutes, expected) => {
    vi.useFakeTimers()
    vi.setSystemTime(new Date('2026-01-01T00:00:00Z'))
    expect(formatRelative(new Date(Date.now() - minutes * 60_000).toISOString())).toBe(expected)
  })

  it('preserves invalid dates for display', () => {
    expect(formatRelative('unknown')).toBe('unknown')
  })
})
