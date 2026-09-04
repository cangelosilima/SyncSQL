import { NO_SCHEMA_LABEL, buildIndex } from './catalog'
import { makeCatalog, makeEdge, makeNode } from '../test/fixtures'

describe('buildIndex', () => {
  it('indexes every node by id', () => {
    const index = buildIndex(makeCatalog({ nodes: [makeNode({ id: 'a' }), makeNode({ id: 'b' })] }))
    expect([...index.byId.keys()].sort()).toEqual(['a', 'b'])
  })

  it('records each edge in both directions', () => {
    const index = buildIndex(
      makeCatalog({
        nodes: [makeNode({ id: 'a' }), makeNode({ id: 'b' })],
        edges: [makeEdge('a', 'b')],
      }),
    )
    expect(index.outgoing.get('a')).toEqual(['b'])
    expect(index.incoming.get('b')).toEqual(['a'])
    expect(index.outgoing.get('b')).toBeUndefined()
  })

  it('keys referenced columns by "from|to" and skips edges that carry none', () => {
    const index = buildIndex(
      makeCatalog({
        nodes: [makeNode({ id: 'a' }), makeNode({ id: 'b' })],
        edges: [makeEdge('a', 'b', ['Id', 'Name']), makeEdge('b', 'a')],
      }),
    )
    expect(index.edgeColumns.get('a|b')).toEqual(['Id', 'Name'])
    expect(index.edgeColumns.has('b|a')).toBe(false)
  })

  it('groups orphaned references by the node whose DDL contains them', () => {
    const index = buildIndex(
      makeCatalog({
        nodes: [makeNode({ id: 'a' })],
        orphanedReferences: [
          { from: 'a', schema: 'dbo', name: 'Gone' },
          { from: 'a', schema: null, name: 'AlsoGone' },
        ],
      }),
    )
    expect(index.orphanedByFrom.get('a')?.map((r) => r.name)).toEqual(['Gone', 'AlsoGone'])
  })

  it('treats a catalog built before orphanedReferences existed as having none', () => {
    const index = buildIndex(makeCatalog({ nodes: [makeNode({ id: 'a' })] }))
    expect(index.orphanedByFrom.size).toBe(0)
  })

  describe('tree', () => {
    it('nests server > database > schema > type > node', () => {
      const index = buildIndex(
        makeCatalog({
          nodes: [makeNode({ id: 'a', server: 'SRV1', database: 'AppDb', schema: 'dbo', type: 'Tables', name: 'Orders' })],
        }),
      )
      const server = index.tree[0]
      expect(server.name).toBe('SRV1')
      expect(server.databases[0].name).toBe('AppDb')
      expect(server.databases[0].schemas[0].name).toBe('dbo')
      expect(server.databases[0].schemas[0].types[0].name).toBe('Tables')
      expect(server.databases[0].schemas[0].types[0].nodes[0].name).toBe('Orders')
    })

    it('sorts servers, databases, types and node names alphabetically', () => {
      const index = buildIndex(
        makeCatalog({
          nodes: [
            makeNode({ id: '1', server: 'SRV2', database: 'Zeta', type: 'Views', name: 'zz' }),
            makeNode({ id: '2', server: 'SRV1', database: 'Alpha', type: 'Views', name: 'bb' }),
            makeNode({ id: '3', server: 'SRV1', database: 'Alpha', type: 'Tables', name: 'aa' }),
          ],
        }),
      )
      expect(index.tree.map((s) => s.name)).toEqual(['SRV1', 'SRV2'])
      expect(index.tree[0].databases.map((d) => d.name)).toEqual(['Alpha'])
      expect(index.tree[0].databases[0].schemas[0].types.map((t) => t.name)).toEqual(['Tables', 'Views'])
    })

    it('buckets schema-less objects under the server-level label and sorts it last', () => {
      const index = buildIndex(
        makeCatalog({
          nodes: [
            makeNode({ id: 'link', schema: null, type: 'LinkedServers', name: 'REPORTING' }),
            makeNode({ id: 'tbl', schema: 'dbo', type: 'Tables', name: 'Orders' }),
          ],
        }),
      )
      expect(index.tree[0].databases[0].schemas.map((s) => s.name)).toEqual(['dbo', NO_SCHEMA_LABEL])
    })
  })
})
