import { afterEach, expect, it, vi } from 'vitest'
import { buildIndex, loadCatalog } from './catalog'
import { makeCatalog, makeEdge, makeNode } from '../test/fixtures'
import { dependencyRows } from './catalogCsv'
import { getColumnConsumers } from './columnLineage'
import { groupRelated } from './grouping'
import { diffLines } from './diff'
afterEach(() => {
  vi.unstubAllGlobals()
  vi.doUnmock('./partitionedCatalog')
})
it('loads legacy catalogs and reports HTTP failure', async () => {
  const catalog = makeCatalog()
  vi.stubGlobal(
    'fetch',
    vi
      .fn()
      .mockResolvedValueOnce({ ok: true, json: async () => catalog })
      .mockResolvedValueOnce({ ok: false, status: 503, statusText: 'Unavailable' }),
  )
  expect((await loadCatalog()).catalog).toEqual(catalog)
  await expect(loadCatalog()).rejects.toThrow('503 Unavailable')
})
it('loads partitioned catalogs through the partition loader', async () => {
  const catalog = { format: 'syncsql-partitioned' }
  const index = buildIndex(makeCatalog())
  const load = vi.fn().mockResolvedValue(index)
  vi.doMock('./partitionedCatalog', () => ({ loadPartitionedCatalog: load }))
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue({ ok: true, json: async () => catalog }))
  expect(await loadCatalog()).toBe(index)
  expect(load).toHaveBeenCalledWith(catalog, '/data/')
})
it('drops missing dependency endpoints, sorts column consumers, and groups by server', () => {
  const nodes = ['b', 'a', 'root'].map((id) => makeNode({ id }))
  const index = buildIndex(
    makeCatalog({
      nodes,
      edges: [
        makeEdge('b', 'root', ['Id']),
        makeEdge('a', 'root', ['Id']),
        makeEdge('missing', 'root', ['Id']),
        makeEdge('root', 'missing'),
      ],
    }),
  )
  expect(dependencyRows(index, 'root')).toHaveLength(2)
  expect(getColumnConsumers(index, 'root', 'Id').map((n) => n.id)).toEqual(['a', 'b'])
  expect(groupRelated(nodes, 'server')).toEqual([{ key: 'SRV1', nodes }])
  expect(groupRelated(nodes, 'database')).toEqual([{ key: 'AppDb', nodes }])
  index.incoming.get('root')!.push('untagged')
  expect(getColumnConsumers(index, 'root', 'Id')).toHaveLength(2)
  expect(diffLines('a\nb', 'a')).toContainEqual({ type: 'delete', text: 'b', oldLineNo: 2, newLineNo: null })
})
it('orders schema-less objects after named schemas regardless of insertion order', () => {
  const index = buildIndex(
    makeCatalog({
      nodes: [
        makeNode({ id: 'none', schema: null }),
        makeNode({ id: 'named', schema: 'dbo' }),
        makeNode({ id: 'another', schema: 'aaa' }),
      ],
    }),
  )
  expect(index.tree[0].databases[0].schemas.map((schema) => schema.name)).toEqual(['aaa', 'dbo', '(server-level)'])
  const reversed = buildIndex(makeCatalog({ nodes: [...index.catalog.nodes].reverse() }))
  expect(reversed.tree[0].databases[0].schemas.map((schema) => schema.name)).toEqual(['aaa', 'dbo', '(server-level)'])
})
