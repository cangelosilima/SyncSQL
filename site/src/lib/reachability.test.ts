import { describe, expect, it } from 'vitest'
import { buildIndex } from './catalog'
import { getReachable } from './reachability'
import { makeCatalog, makeEdge, makeNode } from '../test/fixtures'

/** a -> b -> c, all on SRV1. */
const sameServerIndex = buildIndex(
  makeCatalog({
    nodes: [makeNode({ id: 'a' }), makeNode({ id: 'b' }), makeNode({ id: 'c' })],
    edges: [makeEdge('a', 'b'), makeEdge('b', 'c')],
  }),
)

describe('getReachable', () => {
  it('records hop distance from the seed and excludes the seed itself', () => {
    const reachable = getReachable('a', 'outgoing', sameServerIndex)
    expect(reachable.get('b')).toBe(1)
    expect(reachable.get('c')).toBe(2)
    expect(reachable.has('a')).toBe(false)
  })

  it('walks the incoming direction when asked', () => {
    const reachable = getReachable('c', 'incoming', sameServerIndex)
    expect([...reachable.entries()].sort()).toEqual([
      ['a', 2],
      ['b', 1],
    ])
  })

  it('returns nothing for a seed that is not in the catalog', () => {
    expect(getReachable('missing', 'outgoing', sameServerIndex).size).toBe(0)
  })

  it.each(['incoming', 'outgoing'] as const)('does not traverse %s edges from a missing seed', (direction) => {
    const index = buildIndex(
      makeCatalog({
        nodes: [makeNode({ id: 'known' })],
        edges: [makeEdge('missing', 'known'), makeEdge('known', 'missing')],
      }),
    )
    expect([...getReachable('missing', direction, index)]).toEqual([])
  })

  it.each(['incoming', 'outgoing'] as const)(
    'records a dangling %s neighbor without traversing through it',
    (direction) => {
      const edges = [makeEdge('a', 'missing'), makeEdge('missing', 'b')]
      const index = buildIndex(
        makeCatalog({
          nodes: [makeNode({ id: 'a' }), makeNode({ id: 'b' })],
          edges,
        }),
      )
      const seed = direction === 'outgoing' ? 'a' : 'b'
      expect([...getReachable(seed, direction, index)]).toEqual([['missing', 1]])
    },
  )

  it('does not enter a disconnected component with arbitrary node IDs', () => {
    // This is a valid catalog ID, even though Stryker also uses it when mutating
    // an empty array. Traversal must never introduce nodes outside the frontier.
    const disconnectedId = 'Stryker was here'
    const index = buildIndex(
      makeCatalog({
        nodes: ['a', 'b', disconnectedId, 'unreachable'].map((id) => makeNode({ id })),
        edges: [makeEdge('a', 'b'), makeEdge(disconnectedId, 'unreachable')],
      }),
    )
    expect([...getReachable('a', 'outgoing', index)]).toEqual([['b', 1]])
  })

  it('terminates when the frontier is exhausted with no hop limit', () => {
    expect([...getReachable('a', 'outgoing', sameServerIndex, Infinity)]).toEqual([
      ['b', 1],
      ['c', 2],
    ])
  })

  it('stops at maxHops', () => {
    const reachable = getReachable('a', 'outgoing', sameServerIndex, 1)
    expect([...reachable.keys()]).toEqual(['b'])
  })

  it('records a cross-server neighbor but does not traverse past it', () => {
    // a (SRV1) -> remote (SRV2) -> deep (SRV2): the linked-server hop is
    // reported, the object behind it is not.
    const index = buildIndex(
      makeCatalog({
        nodes: [
          makeNode({ id: 'a', server: 'SRV1' }),
          makeNode({ id: 'remote', server: 'SRV2' }),
          makeNode({ id: 'deep', server: 'SRV2' }),
        ],
        edges: [makeEdge('a', 'remote'), makeEdge('remote', 'deep')],
      }),
    )
    const reachable = getReachable('a', 'outgoing', index)
    expect(reachable.get('remote')).toBe(1)
    expect(reachable.has('deep')).toBe(false)
  })

  it('terminates on a cycle', () => {
    const index = buildIndex(
      makeCatalog({
        nodes: [makeNode({ id: 'a' }), makeNode({ id: 'b' })],
        edges: [makeEdge('a', 'b'), makeEdge('b', 'a')],
      }),
    )
    expect([...getReachable('a', 'outgoing', index).keys()]).toEqual(['b'])
  })
})
