// @vitest-environment node
import { afterEach, expect, it, vi } from 'vitest'
import { loadPartitionedCatalog, PartitionedCatalog, type CatalogManifest } from './partitionedCatalog'
import { makeCatalog, makeNode } from '../test/fixtures'
import { applyFilters, type FilterToken } from './filters'
const file = (n: number) => `_catalog/${n.toString(16).padStart(64, '0')}.json`
function fixture(count = 1) {
  const nodes = Array.from({ length: count }, (_, i) => makeNode({ id: `n${i}` }))
  const manifest: CatalogManifest = {
    ...makeCatalog(),
    format: 'syncsql-partitioned',
    version: 1,
    nodeCount: count,
    edgeCount: 0,
    partitions: nodes.map((node, i) => ({
      server: node.server,
      database: node.database,
      count: 1,
      details: file(i * 4 + 1),
      search: file(i * 4 + 2),
      grants: file(i * 4 + 3),
      edges: file(i * 4 + 4),
      orphanedReferenceCount: 1,
    })),
    summaries: [{ file: file(999), count }],
  }
  const payloads = new Map<string, unknown>([
    [file(999), nodes.map((node, partition) => ({ partition, nodes: [node] }))],
  ])
  nodes.forEach((node, i) => {
    const part = manifest.partitions[i]
    payloads.set(part.details, [node])
    payloads.set(part.search, [{ id: node.id, ddl: 'SELECT needle', sections: [] }])
    payloads.set(part.grants, [])
    payloads.set(part.edges, { edges: [] })
  })
  const fetch = vi.fn(async (url: string) => new Response(JSON.stringify(payloads.get(url))))
  vi.stubGlobal('fetch', fetch)
  return {
    nodes,
    manifest,
    payloads,
    fetch,
    source: new PartitionedCatalog(
      manifest,
      '',
      nodes.map((node) => [node]),
    ),
  }
}
afterEach(() => vi.unstubAllGlobals())
it('validates format, summary assignments, counts and object uniqueness', async () => {
  for (const kind of [
    'format',
    'version',
    'summary-count',
    'unknown-part',
    'duplicate-part',
    'part-count',
    'missing-part',
    'node-count',
    'duplicate-node',
    'path',
  ]) {
    const { manifest, payloads, nodes } = fixture(2)
    if (kind === 'format') manifest.format = 'unknown' as never
    if (kind === 'version') manifest.version = 2 as never
    if (kind === 'summary-count') manifest.summaries[0].count = 100
    if (kind === 'unknown-part') payloads.set(file(999), [{ partition: 3, nodes }])
    if (kind === 'duplicate-part')
      payloads.set(file(999), [
        { partition: 0, nodes: [nodes[0]] },
        { partition: 0, nodes: [nodes[1]] },
      ])
    if (kind === 'part-count') manifest.partitions[0].count = 2
    if (kind === 'missing-part') {
      manifest.summaries = []
      manifest.nodeCount = 0
    }
    if (kind === 'node-count') manifest.nodeCount = 3
    if (kind === 'duplicate-node') nodes[1].id = nodes[0].id
    if (kind === 'path') manifest.summaries[0].file = '../escape.json'
    await expect(loadPartitionedCatalog(manifest, '')).rejects.toThrow()
  }
})
it('restores absent summary arrays and reports HTTP and missing decompression support', async () => {
  const { manifest, payloads } = fixture()
  delete manifest.recentChanges
  delete manifest.coChangePairs
  payloads.set(file(999), [
    {
      partition: 0,
      nodes: [{ id: 'n0', server: 'S', database: 'D', schema: null, type: 'Tables', name: 'N', qualifiedName: 'N' }],
    },
  ])
  const index = await loadPartitionedCatalog(manifest, '')
  expect(index.catalog.nodes[0]).toMatchObject({ columns: [], grants: [], metrics: [] })
  expect(index.catalog.recentChanges).toEqual([])
  expect(index.catalog.coChangePairs).toEqual([])
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('', { status: 404 })))
  await expect(loadPartitionedCatalog(manifest, '')).rejects.toThrow('404')
  manifest.summaries[0].file += '.gz'
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(new Uint8Array([0x1f, 0x8b]))))
  vi.stubGlobal('DecompressionStream', undefined)
  await expect(loadPartitionedCatalog(manifest, '')).rejects.toThrow('current browser')
})
it('validates missing detail, search and permission records and retries failed details', async () => {
  const { source, payloads, manifest, nodes } = fixture()
  expect(await source.object('absent')).toBeUndefined()
  payloads.set(manifest.partitions[0].search, [])
  await expect(source.object('n0')).rejects.toThrow('search partition')
  payloads.set(manifest.partitions[0].search, [{ id: 'n0', ddl: '', sections: [] }])
  nodes[0].grantCount = 1
  await expect(source.object('n0')).rejects.toThrow('permission partition')
  await expect(source.permissions(['n0', 'unknown'])).rejects.toThrow('permission partition')
  nodes[0].grantCount = 0
  payloads.set(manifest.partitions[0].details, [])
  await expect(source.object('n0')).rejects.toThrow('detail partition')
  payloads.set(manifest.partitions[0].details, nodes)
  expect(await source.object('n0')).toMatchObject({ id: 'n0', grants: [] })
})
it('evicts least recently used detail partitions and shares concurrent edge requests', async () => {
  const { source, fetch, manifest } = fixture(9)
  for (let i = 0; i < 9; i++) await source.object(`n${i}`)
  fetch.mockClear()
  await source.object('n0')
  expect(fetch).toHaveBeenCalledTimes(3)
  fetch.mockClear()
  await Promise.all([source.edges(['n0']), source.edges(['n0'])])
  expect(fetch).toHaveBeenCalledTimes(1)
  expect(fetch).toHaveBeenCalledWith(manifest.partitions[0].edges, expect.anything())
  expect((await source.neighborhood('unknown', 1, 1)).catalog.nodes).toHaveLength(9)
})
it('loads alert partitions, filters permission records and honors cancellation', async () => {
  const { source, manifest, payloads } = fixture(2)
  delete manifest.partitions[1].orphanedReferenceCount
  payloads.set(manifest.partitions[0].edges, {
    edges: [],
    orphanedReferences: [{ from: 'n0', schema: null, name: 'missing' }],
    systemReferences: [{ from: 'n0', schema: 'sys', name: 'objects' }],
  })
  // Alert metadata advertises the reference collection.
  source.index.catalog.orphanedReferences = []
  expect((await source.alerts()).orphanedByFrom.get('n0')).toHaveLength(1)
  payloads.set(manifest.partitions[0].grants, [
    { id: 'n0', grants: [] },
    { id: 'other', grants: [] },
  ])
  expect(await source.permissions(['n0', 'missing'])).toEqual(new Map([['n0', []]]))
  const controller = new AbortController()
  controller.abort()
  await expect(source.edges(['n0'], controller.signal)).rejects.toMatchObject({ name: 'AbortError' })
  await expect(source.alerts(controller.signal)).rejects.toMatchObject({ name: 'AbortError' })
  await expect(source.permissions(['n0'], controller.signal)).rejects.toMatchObject({ name: 'AbortError' })
})
const tokens: FilterToken[] = [{ id: 'q', attribute: 'ddl', operator: 'contains', values: ['needle'] }]
it('does not let a failed evicted request discard a newer cached request', async () => {
  const { source, fetch, manifest } = fixture(9)
  let reject!: (reason: Error) => void
  fetch.mockImplementationOnce(
    () =>
      new Promise((_, fail) => {
        reject = fail
      }),
  )
  const first = expect(source.object('n0')).rejects.toThrow('old failure')
  for (let i = 1; i < 9; i++) await source.object(`n${i}`)
  expect(await source.object('n0')).toMatchObject({ id: 'n0' })
  reject(new Error('old failure'))
  await first
  fetch.mockClear()
  expect(await source.object('n0')).toMatchObject({ id: 'n0' })
  expect(fetch).not.toHaveBeenCalledWith(manifest.partitions[0].details, expect.anything())
})
it('ignores malformed linked references without a connection identifier', async () => {
  const { source, manifest, payloads } = fixture()
  payloads.set(manifest.partitions[0].edges, { edges: [], linkedServerReferences: [{ from: 'n0', name: 'missing' }] })
  expect((await source.edges()).catalog.linkedServerReferences).toEqual([])
})
it.each(['success', 'error', 'cancel'])('searches in a worker and cleans up on %s', async (outcome) => {
  const { source, nodes } = fixture()
  const controller = new AbortController()
  const terminate = vi.fn()
  vi.stubGlobal(
    'Worker',
    class {
      onmessage!: (event: { data: string[] }) => void
      onerror!: () => void
      terminate = terminate
      postMessage({ nodes: data, tokens: filters }: { nodes: typeof nodes; tokens: FilterToken[] }) {
        queueMicrotask(() => {
          if (outcome === 'cancel') controller.abort()
          else if (outcome === 'error') this.onerror()
          else this.onmessage({ data: applyFilters(data, filters).map((node) => node.id) })
        })
      }
    },
  )
  const result = source.search(nodes, tokens, controller.signal)
  if (outcome === 'success') expect(await result).toEqual(nodes)
  else await expect(result).rejects.toThrow(outcome === 'error' ? 'Catalog search failed' : 'Cancelled')
  expect(terminate).toHaveBeenCalledOnce()
})
it('rejects missing search records and skips fetching for metadata-only filters', async () => {
  const { source, nodes, payloads, manifest, fetch } = fixture()
  expect(await source.search(nodes, [], new AbortController().signal)).toEqual(nodes)
  expect(fetch).not.toHaveBeenCalled()
  payloads.set(manifest.partitions[0].search, [])
  await expect(source.search(nodes, tokens, new AbortController().signal)).rejects.toThrow('search partition')
})
