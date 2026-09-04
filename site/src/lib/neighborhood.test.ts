import { describe, expect, it } from 'vitest'
import { buildIndex } from './catalog'
import { getEdgeColumns, getNeighborhoodIds } from './neighborhood'
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
