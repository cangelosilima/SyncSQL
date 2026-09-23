// @vitest-environment node
import { afterAll, afterEach, beforeAll, expect, it, vi } from 'vitest'
import { mkdtemp, readFile, rm, stat, writeFile } from 'node:fs/promises'
import { tmpdir } from 'node:os'
import path from 'node:path'
import { gunzipSync } from 'node:zlib'
import { partitionCatalog } from './partition-catalog.mjs'
import { loadPartitionedCatalog, type CatalogManifest } from '../src/lib/partitionedCatalog'
import { buildIndex } from '../src/lib/catalog'
import { getDirectionalNeighborhood } from '../src/lib/neighborhood'
import { getTopReferencedTables } from '../src/lib/analytics'
import { detectMetricAnomalies } from '../src/lib/anomalies'
import { applyFilters, type FilterToken } from '../src/lib/filters'
import { makeCatalog, makeEdge, makeNode } from '../src/test/fixtures'
import type { Catalog } from '../src/types'

let temporary: string
let output: string
let manifest: CatalogManifest
let original: Catalog
const fetchFile = async (url: string | URL | Request) =>
  new Response(await readFile(path.join(output, String(url).replace('/project/data/', ''))))

beforeAll(async () => {
  temporary = await mkdtemp(path.join(tmpdir(), 'syncsql-partitions-'))
  output = path.join(temporary, 'data')
  const input = new URL('../public/data/catalog.json', import.meta.url)
  original = JSON.parse(await readFile(input, 'utf8'))
  const source = path.join(temporary, 'source.json')
  await writeFile(source, JSON.stringify(original))
  manifest = await partitionCatalog(source, output, { maxNodes: 4, maxBytes: 16_384 })
}, 30_000)

afterEach(() => vi.unstubAllGlobals())
afterAll(async () => {
  await rm(temporary, { recursive: true, force: true })
})

it('accepts compressed partitions already decoded by the HTTP host', async () => {
  vi.stubGlobal('fetch', async (url: string | URL | Request) => {
    const data = await readFile(path.join(output, String(url).replace('/project/data/', '')))
    return new Response(gunzipSync(data), { headers: { 'Content-Encoding': 'gzip' } })
  })
  const index = await loadPartitionedCatalog(manifest, '/project/data/')
  expect(index.catalog.nodes).toHaveLength(original.nodes.length)
  expect(await index.source!.object(original.nodes[0].id)).toEqual(original.nodes[0])
})

it('prunes obsolete payloads while preserving active and unrelated files', async () => {
  const destination = path.join(temporary, 'pruned')
  await partitionCatalog(path.join(output, 'catalog.json'), destination)
  const stale = path.join(destination, '_catalog', `${'0'.repeat(64)}.json.gz`)
  const unrelated = path.join(destination, '_catalog', 'notes.txt')
  await writeFile(stale, 'obsolete')
  await writeFile(unrelated, 'keep')
  await partitionCatalog(path.join(output, 'catalog.json'), destination, { prune: true })
  await expect(stat(stale)).rejects.toThrow()
  expect(await readFile(unrelated, 'utf8')).toBe('keep')
  for (const summary of manifest.summaries)
    expect((await stat(path.join(destination, summary.file))).size).toBeGreaterThan(0)
})

it('starts from summaries only and restores exact details on demand', async () => {
  const fetch = vi.fn(fetchFile)
  vi.stubGlobal('fetch', fetch)
  const index = await loadPartitionedCatalog(manifest, '/project/data/')
  expect(index.catalog.nodes).toHaveLength(original.nodes.length)
  expect(index.catalog.generatedAt).toBe(original.generatedAt)
  expect(index.catalog.example).toEqual(original.example)
  expect(index.catalog.edges).toEqual([])
  expect(fetch.mock.calls.map(([url]) => String(url)).sort()).toEqual(
    manifest.summaries.map((s) => `/project/data/${s.file}`).sort(),
  )
  expect(index.catalog.nodes.every((node) => !node.ddl && !node.history.length && !node.sections.length)).toBe(true)
  expect(index.catalog.nodes.every((node) => !node.columns.length && !node.grants.length)).toBe(true)
  const source = original.nodes.find((node) => node.history.length || node.sections.length)!
  const calls = fetch.mock.calls.length
  expect(await index.source!.object(source.id)).toEqual(source)
  expect(await index.source!.object(source.id)).toEqual(source)
  expect(fetch.mock.calls.length).toBe(calls + 3)
  expect(index.byId.get(source.id)?.ddl).toBe('')
  expect(getTopReferencedTables(index).map(({ node, ...usage }) => ({ id: node.id, ...usage }))).toEqual(
    getTopReferencedTables(buildIndex(original)).map(({ node, ...usage }) => ({ id: node.id, ...usage })),
  )
  expect(detectMetricAnomalies(index.catalog.nodes)).toEqual(detectMetricAnomalies(original.nodes))
  const grants = await index.source!.permissions(original.nodes.map((node) => node.id))
  for (const node of original.nodes) expect(grants.get(node.id) ?? []).toEqual(node.grants)
})

it('preserves incoming, multi-hop, cyclic and attributed cross-server lineage without loading details', async () => {
  const fetch = vi.fn(fetchFile)
  vi.stubGlobal('fetch', fetch)
  const index = await loadPartitionedCatalog(manifest, '/project/data/')
  const baseline = buildIndex(original)
  for (const route of original.example!.paths) {
    const id = route.nodes[0]
    const loaded = await index.source!.neighborhood(id, 6, 3)
    expect(getDirectionalNeighborhood(loaded, id, 6, 3).sort()).toEqual(
      getDirectionalNeighborhood(baseline, id, 6, 3).sort(),
    )
  }
  const loaded = await index.source!.edges()
  const sorted = <T>(items: T[]) => items.map((item) => JSON.stringify(item)).sort()
  expect(sorted(loaded.catalog.edges)).toEqual(sorted(original.edges))
  expect(sorted(loaded.catalog.linkedServerReferences!)).toEqual(sorted(original.linkedServerReferences!))
  expect(sorted(loaded.catalog.systemReferences!)).toEqual(sorted(original.systemReferences!))
  expect(loaded.edgeColumns).toEqual(baseline.edgeColumns)
  expect(loaded.dynamicEdges).toEqual(baseline.dynamicEdges)
  const detailUrls = new Set(manifest.partitions.map((part) => `/project/data/${part.details}`))
  expect(fetch.mock.calls.some(([url]) => detailUrls.has(String(url)))).toBe(false)
})

it('keeps substring and combined permission/DDL search semantics without retaining SQL on summary nodes', async () => {
  vi.stubGlobal('fetch', vi.fn(fetchFile))
  const index = await loadPartitionedCatalog(manifest, '/project/data/')
  for (const tokens of [
    [{ id: 'a', attribute: 'ddl', operator: 'contains', values: ['select'] }],
    [{ id: 'a', attribute: null, operator: 'contains', values: ['items'] }],
    [
      { id: 'a', attribute: 'ddl', operator: 'is-not', values: ['SELECT 1'] },
      { id: 'b', attribute: 'grantee', operator: 'contains', values: ['READ'] },
    ],
    [
      { id: 'a', attribute: 'server', operator: 'is', values: [original.nodes[0].server] },
      { id: 'b', attribute: 'ddl', operator: 'contains', values: ['CREATE'] },
    ],
  ] as FilterToken[][]) {
    const matches = await index.source!.search(index.catalog.nodes, tokens, new AbortController().signal)
    expect(matches.map((node) => node.id).sort()).toEqual(
      applyFilters(original.nodes, tokens)
        .map((node) => node.id)
        .sort(),
    )
  }
  expect(index.catalog.nodes.every((node) => node.ddl === '')).toBe(true)
  const aborted = new AbortController()
  aborted.abort()
  await expect(
    index.source!.search(
      index.catalog.nodes,
      [{ id: 'a', attribute: 'ddl', operator: 'contains', values: ['x'] }],
      aborted.signal,
    ),
  ).rejects.toMatchObject({ name: 'AbortError' })
})

it('retries failed partition requests and reports missing resources rather than empty data', async () => {
  const fetch = vi.fn(fetchFile)
  vi.stubGlobal('fetch', fetch)
  const index = await loadPartitionedCatalog(manifest, '/project/data/')
  const id = original.nodes[0].id
  fetch.mockImplementationOnce(async () => new Response('', { status: 404 }))
  await expect(index.source!.object(id)).rejects.toThrow('Reload')
  expect(await index.source!.object(id)).toEqual(original.nodes[0])
  fetch.mockImplementationOnce(async () => new Response('', { status: 503 }))
  await expect(index.source!.edges([id])).rejects.toThrow('Reload')
  await expect(index.source!.edges([id])).resolves.toBeDefined()
  expect(await index.source!.object('missing')).toBeUndefined()
  await expect(index.source!.neighborhood('missing', 2, 2)).resolves.toBeDefined()
})

// This round trip copies and regenerates the complete partition tree on disk,
// so use the same filesystem timeout as setup on slower Windows CI runners.
it('uses stable content hashes, supports pre-partitioned inputs and rejects broken manifests', async () => {
  const second = path.join(temporary, 'second')
  const copy = await partitionCatalog(path.join(output, 'catalog.json'), second)
  expect(copy).toEqual(manifest)
  const regenerated = await partitionCatalog(path.join(temporary, 'source.json'), path.join(temporary, 'third'), {
    maxNodes: 4,
    maxBytes: 16_384,
  })
  expect(regenerated).toEqual(manifest)
  for (const summary of copy.summaries) expect((await stat(path.join(second, summary.file))).size).toBeGreaterThan(0)
  vi.stubGlobal('fetch', vi.fn(fetchFile))
  await expect(
    loadPartitionedCatalog({ ...manifest, version: 2 } as unknown as CatalogManifest, '/project/data/'),
  ).rejects.toThrow('Unsupported')
  await expect(loadPartitionedCatalog({ ...manifest, nodeCount: 0 }, '/project/data/')).rejects.toThrow('count')
  await expect(
    loadPartitionedCatalog({ ...manifest, summaries: [{ file: '../secret', count: 1 }] }, '/project/data/'),
  ).rejects.toThrow('path')
}, 30_000)

it.each([undefined, [], [{ from: 'orders', name: 'missing', schema: null }]])(
  'preserves unavailable, empty and populated orphan analysis: %j',
  async (orphanedReferences) => {
    const source = path.join(temporary, 'orphan-state.json')
    const destination = path.join(temporary, 'orphan-state')
    await writeFile(source, JSON.stringify(makeCatalog({ nodes: [makeNode({ id: 'orders' })], orphanedReferences })))
    await partitionCatalog(source, destination)
    // Read the serialized manifest: undefined must remain absent after publication.
    const published = JSON.parse(await readFile(path.join(destination, 'catalog.json'), 'utf8'))
    expect(published.orphanedReferenceCount).toBe(orphanedReferences?.length)
    expect(Object.hasOwn(published, 'orphanedReferenceCount')).toBe(orphanedReferences !== undefined)
    vi.stubGlobal(
      'fetch',
      async (url: string | URL | Request) =>
        new Response(await readFile(path.join(destination, String(url).replace('/project/data/', '')))),
    )
    const index = await loadPartitionedCatalog(published, '/project/data/')
    expect(index.catalog.orphanedReferences).toEqual(orphanedReferences === undefined ? undefined : [])
    const alerts = await index.source!.alerts()
    expect(alerts.catalog.orphanedReferences).toEqual(orphanedReferences)
    // Browsing an edge partition with an empty reference array must not invent analysis.
    await index.source!.edges(['orders'])
    expect(index.catalog.orphanedReferences).toEqual(orphanedReferences)
  },
)

it('handles byte boundaries, Unicode identities, empty snapshots and orphan-only partitions', async () => {
  const source = path.join(temporary, 'boundary.json')
  const node = makeNode({
    id: '服务器/😀',
    database: '../数据库',
    ddl: "SELECT N'😀'\n".repeat(100),
    sections: [{ title: 'Index', content: 'UNIQUE_SECTION_TOKEN' }],
    metrics: [1, 2, 3].map((i) => ({
      capturedAt: `2026-01-0${i}`,
      rowCount: 1000 * i,
      reservedKB: null,
      dataKB: null,
      indexKB: null,
      indexes: [],
      statistics: [],
    })),
  })
  const data = makeCatalog({
    nodes: [node, makeNode({ id: 'other' })],
    edges: [makeEdge('other', node.id)],
    orphanedReferences: [{ from: node.id, name: 'missing', schema: null }],
  })
  await writeFile(source, JSON.stringify(data))
  const boundaryOutput = path.join(temporary, 'boundary')
  const result = await partitionCatalog(source, boundaryOutput, { maxBytes: 128 })
  expect(result.partitions).toHaveLength(2)
  const oldOutput = output
  output = boundaryOutput
  vi.stubGlobal('fetch', vi.fn(fetchFile))
  try {
    const index = await loadPartitionedCatalog(result, '/project/data/')
    const alerts = await index.source!.alerts()
    expect(alerts.catalog.orphanedReferences).toEqual(data.orphanedReferences)
    expect(detectMetricAnomalies(index.catalog.nodes).map(({ message }) => message)).toEqual(
      detectMetricAnomalies(data.nodes).map(({ message }) => message),
    )
    const matches = await index.source!.search(
      index.catalog.nodes,
      [{ id: 'q', attribute: 'ddl', operator: 'contains', values: ['unique_section_token'] }],
      new AbortController().signal,
    )
    expect(matches.map((n) => n.id)).toEqual([node.id])
    expect(await index.source!.object(node.id)).toEqual(node)
  } finally {
    output = oldOutput
  }
  await writeFile(source, JSON.stringify(makeCatalog()))
  const empty = await partitionCatalog(source, path.join(temporary, 'empty'))
  expect((await loadPartitionedCatalog(empty, '/project/data/')).catalog.nodes).toEqual([])
  await writeFile(source, JSON.stringify(makeCatalog({ edges: [makeEdge('missing', 'also-missing')] })))
  await expect(partitionCatalog(source, path.join(temporary, 'broken'))).rejects.toThrow('unknown object')
})
