import { describe, expect, it } from 'vitest'
import { buildIndex } from './catalog'
import { getDirectionalNeighborhood, getEdgeColumns, getNeighborhoodIds, groupIntermediateLayers, retainConnectingPaths } from './neighborhood'
import { makeCatalog, makeEdge, makeNode } from '../test/fixtures'

// upstream -> root -> downstream -> far
const index = buildIndex(
  makeCatalog({
    nodes: ['upstream', 'root', 'downstream', 'far', 'island'].map((id) => makeNode({ id })),
    edges: [makeEdge('upstream', 'root'), makeEdge('root', 'downstream', ['Id']), makeEdge('downstream', 'far')],
  }),
)

describe('getNeighborhoodIds', () => {
  it('always includes the root, even with zero hops', () => {
    expect(getNeighborhoodIds(index, 'root', 0)).toEqual(['root'])
  })

  it('expands in both directions at one hop', () => {
    expect(getNeighborhoodIds(index, 'root', 1).sort()).toEqual(['downstream', 'root', 'upstream'])
  })

  it('reaches further nodes with more hops', () => {
    expect(getNeighborhoodIds(index, 'root', 2).sort()).toEqual(['downstream', 'far', 'root', 'upstream'])
  })

  it('never returns a node with no path to the root', () => {
    expect(getNeighborhoodIds(index, 'root', 10)).not.toContain('island')
  })

  it('visits each node once even when hops exceed the graph diameter', () => {
    const ids = getNeighborhoodIds(index, 'root', 99)
    expect(new Set(ids).size).toBe(ids.length)
  })
})

describe('getEdgeColumns', () => {
  it('returns the columns recorded for the edge', () => {
    expect(getEdgeColumns(index, 'root', 'downstream')).toEqual(['Id'])
  })

  it('returns an empty array for an edge with no recorded columns', () => {
    expect(getEdgeColumns(index, 'upstream', 'root')).toEqual([])
  })

  it('is direction-sensitive', () => {
    expect(getEdgeColumns(index, 'downstream', 'root')).toEqual([])
  })
})

describe('directional expansion and connecting paths', () => {
  it.each(['LinkedServers', 'DatabaseLinks'])('shows one hop beyond %s on both sides, without unrelated branches', type => {
    const linked = buildIndex(makeCatalog({
      nodes: ['root', 'caller', 'target', 'callerParent', 'targetChild', 'sibling'].map(id => makeNode({ id })).concat([
        makeNode({ id: 'incomingLink', type }), makeNode({ id: 'outgoingLink', type }),
      ]),
      edges: [makeEdge('callerParent', 'caller'), makeEdge('caller', 'incomingLink'), makeEdge('incomingLink', 'root'),
        makeEdge('root', 'outgoingLink'), makeEdge('outgoingLink', 'target'), makeEdge('target', 'targetChild'),
        makeEdge('incomingLink', 'sibling'), makeEdge('sibling', 'outgoingLink')],
    }))
    expect(getDirectionalNeighborhood(linked, 'root', 1, 1).sort()).toEqual(['caller', 'incomingLink', 'outgoingLink', 'root', 'target'])
    expect(getDirectionalNeighborhood(linked, 'root', 1, 0).sort()).toEqual(['outgoingLink', 'root', 'target'])
    expect(getDirectionalNeighborhood(linked, 'root', 0, 1).sort()).toEqual(['caller', 'incomingLink', 'root'])
    expect(getDirectionalNeighborhood(linked, 'root', 0, 0)).toEqual(['root'])
    expect(getDirectionalNeighborhood(linked, 'outgoingLink', 0, 0)).toEqual(['outgoingLink'])
    expect(getDirectionalNeighborhood(linked, 'callerParent', 2, 0).sort()).toEqual(['caller', 'callerParent', 'incomingLink', 'root', 'sibling'])
  })
  it('keeps the extra hop bounded across consecutive links and cycles', () => {
    const linked = buildIndex(makeCatalog({
      nodes: ['root', 'a', 'b', 'c'].map(id => makeNode({ id, type: 'DatabaseLinks' })),
      edges: [makeEdge('root', 'a'), makeEdge('a', 'root'), makeEdge('a', 'b'), makeEdge('b', 'c')],
    }))
    expect(getDirectionalNeighborhood(linked, 'root', 1, 0)).toEqual(['root', 'a', 'b'])
  })
  const directed = buildIndex(makeCatalog({
    nodes: ['root', 'dep', 'far', 'user', 'caller', 'sibling'].map(id => makeNode({ id })),
    edges: [makeEdge('root', 'dep'), makeEdge('dep', 'far'), makeEdge('user', 'root'), makeEdge('caller', 'user'), makeEdge('sibling', 'dep'), makeEdge('far', 'root')],
  }))
  it('expands each side independently without turning onto sibling branches', () => {
    expect(getDirectionalNeighborhood(directed, 'root', 2, 0).sort()).toEqual(['dep', 'far', 'root'])
    expect(getDirectionalNeighborhood(directed, 'root', 0, 2).sort()).toEqual(['caller', 'dep', 'far', 'root', 'user'])
    expect(getDirectionalNeighborhood(directed, 'root', 0, 0)).toEqual(['root'])
    expect(getDirectionalNeighborhood(directed, 'root', 6, 0)).not.toContain('sibling')
    const cyclic = getDirectionalNeighborhood(directed, 'root', 6, 6)
    expect(new Set(cyclic).size).toBe(cyclic.length)
  })
  it('retains filtered intermediate objects without unrelated branches', () => {
    expect(retainConnectingPaths(index, 'root', getNeighborhoodIds(index, 'root', 2), new Set(['far']))).toEqual(['root', 'downstream', 'far'])
    expect(retainConnectingPaths(index, 'root', getNeighborhoodIds(index, 'root', 2), new Set(['island']))).toEqual(['root'])
  })
  it('groups intermediate types and hops while preserving focus and terminal objects', () => {
    const groupedIndex = buildIndex(makeCatalog({
      nodes: ['root', 'a', 'b', 'end', 'leaf'].map(id => makeNode({ id })),
      edges: [makeEdge('root', 'a'), makeEdge('root', 'b'), makeEdge('a', 'end'), makeEdge('b', 'end'), makeEdge('root', 'leaf')],
    }))
    const result = groupIntermediateLayers(groupedIndex, 'root', [...groupedIndex.byId.keys()])
    expect(result.nodeIds).toEqual(['root', 'end', 'leaf'])
    expect(result.bundles).toEqual([expect.objectContaining({ direction: 'outgoing', hop: 1, memberIds: ['a', 'b'] })])
  })
})
