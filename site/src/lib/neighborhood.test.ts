import { describe, expect, it } from 'vitest'
import { buildIndex } from './catalog'
import { getDirectionalNeighborhood, getEdgeColumns, getNeighborhoodIds, groupIntermediateLayers, retainConnectingPaths } from './neighborhood'
import { makeCatalog, makeEdge, makeNode } from '../test/fixtures'
import snapshot from '../../public/data/catalog.json'
import type { Catalog } from '../types'

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
      linkedServerReferences: [
        { linkedServer: 'incomingLink', from: 'caller', to: 'root', schema: null, name: 'root' },
        { linkedServer: 'incomingLink', from: 'caller', to: 'sibling', schema: null, name: 'sibling' },
        { linkedServer: 'outgoingLink', from: 'root', to: 'target', schema: null, name: 'target' },
      ],
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
      linkedServerReferences: [
        { linkedServer: 'a', from: 'root', to: 'root', schema: null, name: 'root' },
        { linkedServer: 'a', from: 'root', to: 'b', schema: null, name: 'b' },
      ],
    }))
    expect(getDirectionalNeighborhood(linked, 'root', 1, 0)).toEqual(['root', 'a', 'b'])
  })
  it.each(['LinkedServers', 'DatabaseLinks'])('preserves caller/target pairs across a shared %s at every hop limit', type => {
    const shared = buildIndex(makeCatalog({
      nodes: ['caller', 'target', 'otherCaller', 'otherTarget', 'unresolvedCaller'].map(id => makeNode({ id }))
        .concat(makeNode({ id: 'link', type })),
      edges: [makeEdge('caller', 'link'), makeEdge('otherCaller', 'link'), makeEdge('unresolvedCaller', 'link'),
        makeEdge('link', 'target'), makeEdge('link', 'otherTarget')],
      linkedServerReferences: [
        { linkedServer: 'link', from: 'caller', to: 'target', schema: null, name: 'target' },
        { linkedServer: 'link', from: 'otherCaller', to: 'otherTarget', schema: null, name: 'otherTarget' },
        { linkedServer: 'link', from: 'unresolvedCaller', to: null, schema: null, name: 'missing' },
      ],
    }))
    for (const hops of [1, 2, 3, 20]) {
      expect(getDirectionalNeighborhood(shared, 'caller', hops, 0).sort()).toEqual(['caller', 'link', 'target'])
      expect(getDirectionalNeighborhood(shared, 'target', 0, hops).sort()).toEqual(['caller', 'link', 'target'])
      expect(getDirectionalNeighborhood(shared, 'unresolvedCaller', hops, 0).sort()).toEqual(['link', 'unresolvedCaller'])
    }
    // Focusing the connection itself intentionally shows all of its uses.
    expect(getDirectionalNeighborhood(shared, 'link', 1, 1).sort()).toEqual([...shared.byId.keys()].sort())
  })

  it.each(['outgoing', 'incoming'])('revisits a shared link through distinct %s object paths', direction => {
    const edge = (from: string, to: string) => direction === 'outgoing' ? makeEdge(from, to) : makeEdge(to, from)
    const reference = (from: string, to: string) => ({
      linkedServer: 'link', schema: null, name: direction === 'outgoing' ? to : from,
      from: direction === 'outgoing' ? from : to, to: direction === 'outgoing' ? to : from,
    })
    const shared = buildIndex(makeCatalog({
      nodes: ['root', 'a', 'b', 'c', 'd', 'end'].map(id => makeNode({ id }))
        .concat(makeNode({ id: 'link', type: 'LinkedServers' })),
      edges: [edge('root', 'a'), edge('root', 'b'), edge('b', 'c'),
        edge('a', 'link'), edge('c', 'link'), edge('link', 'd'), edge('link', 'end')],
      linkedServerReferences: [reference('a', 'd'), reference('c', 'end')],
    }))
    const expand = (hops: number) => getDirectionalNeighborhood(shared, 'root', direction === 'outgoing' ? hops : 0, direction === 'incoming' ? hops : 0).sort()
    expect(expand(2)).toEqual(['a', 'b', 'c', 'd', 'link', 'root'])
    expect(expand(3)).toEqual([...shared.byId.keys()].sort())
  })

  it('stops at an unattributed connection instead of guessing its object flow', () => {
    const legacy = buildIndex(makeCatalog({
      nodes: [makeNode({ id: 'caller' }), makeNode({ id: 'target' }), makeNode({ id: 'link', type: 'DatabaseLinks' })],
      edges: [makeEdge('caller', 'link'), makeEdge('link', 'target')],
    }))
    expect(getDirectionalNeighborhood(legacy, 'caller', 20, 0)).toEqual(['caller', 'link'])
    expect(getDirectionalNeighborhood(legacy, 'target', 0, 20)).toEqual(['target', 'link'])
    expect(getDirectionalNeighborhood(legacy, 'link', 1, 1).sort()).toEqual(['caller', 'link', 'target'])
  })

  it('keeps the benchmark ITEMS flow separate from P_CIRCLE even at twenty hops', () => {
    const benchmark = buildIndex(snapshot as unknown as Catalog)
    const root = 'ATLAS_SQL/Commerce/Tables/ORDER_ENTRY/ITEMS'
    for (const hops of [1, 2, 3, 20]) {
      const names = getDirectionalNeighborhood(benchmark, root, 0, hops).map(id => benchmark.byId.get(id)!.qualifiedName)
      expect(names).toContain('PROCUREMENT.P_ONE')
      expect(names).toContain('PROCUREMENT.P_TWO')
      expect(names).not.toContain('PROCUREMENT.P_CIRCLE')
      expect(names).not.toContain('ORDER_ENTRY.P_CIRCLE')
      expect(names).not.toContain('PROCUREMENT.P_STOCK')
    }
    const circle = 'ATLAS_SQL/Commerce/StoredProcedures/ORDER_ENTRY/P_CIRCLE'
    const names = getDirectionalNeighborhood(benchmark, circle, 20, 20).map(id => benchmark.byId.get(id)!.qualifiedName)
    expect(names).toContain('PROCUREMENT.P_CIRCLE')
    expect(names).not.toContain('ORDER_ENTRY.ITEMS')
    expect(names).not.toContain('PROCUREMENT.P_ONE')
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
