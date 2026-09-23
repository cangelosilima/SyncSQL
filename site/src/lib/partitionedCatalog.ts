import type { Catalog, CatalogEdge, CatalogGrant, CatalogNode, CatalogSection } from '../types'
import { buildIndex, type CatalogIndex } from './catalog'
import { applyFilters, type FilterToken } from './filters'
import { getDirectionalNeighborhood } from './neighborhood'

export interface CatalogPartition {
  server: string
  database: string
  count: number
  details: string
  edges: string
  search: string
  grants: string
  orphanedReferenceCount?: number
}

export interface CatalogManifest extends Omit<Catalog, 'nodes' | 'edges'> {
  format: 'syncsql-partitioned'
  version: 1
  nodeCount: number
  edgeCount: number
  partitions: CatalogPartition[]
  summaries: { file: string; count: number }[]
}

type EdgePayload = Pick<Catalog, 'edges' | 'orphanedReferences' | 'systemReferences' | 'linkedServerReferences'>
type SearchRecord = { id: string; ddl: string; sections: CatalogSection[] }

async function readPartition<T>(base: string, file: string, signal?: AbortSignal): Promise<T> {
  if (!/^_catalog\/[a-f0-9]{64}\.json(?:\.gz)?$/.test(file)) throw new Error(`Invalid catalog partition path: ${file}`)
  const response = await fetch(`${base}${file}`, { signal })
  if (!response.ok)
    throw new Error(
      `Failed to load catalog partition (${response.status}). Reload the page to use the latest snapshot.`,
    )
  // Pages need no custom headers. Sniff the body because fetch may already have
  // decoded it when a host supplies Content-Encoding (or compresses .gz again).
  if (file.endsWith('.gz')) {
    const bytes = await response.arrayBuffer()
    const header = new Uint8Array(bytes, 0, Math.min(2, bytes.byteLength))
    if (header[0] !== 0x1f || header[1] !== 0x8b) return new Response(bytes).json() as Promise<T>
    if (typeof DecompressionStream === 'undefined')
      throw new Error('This browser cannot open compressed catalogs. Please use a current browser.')
    return new Response(new Blob([bytes]).stream().pipeThrough(new DecompressionStream('gzip'))).json() as Promise<T>
  }
  return response.json() as Promise<T>
}

async function parallel<T>(items: T[], action: (item: T) => Promise<void>) {
  let next = 0
  await Promise.all(
    Array.from({ length: Math.min(4, items.length) }, async () => {
      while (next < items.length) await action(items[next++])
    }),
  )
}

function checkAbort(signal?: AbortSignal) {
  if (signal?.aborted) throw new DOMException('Cancelled', 'AbortError')
}

export class PartitionedCatalog {
  readonly index: CatalogIndex
  private routes = new Map<string, number>()
  private loadedEdges = new Set<number>()
  private edgeRequests = new Map<number, Promise<void>>()
  private edgeKeys = new Set<string>()
  private referenceKeys = new Set<string>()
  private details = new Map<number, Promise<CatalogNode[]>>()

  constructor(
    readonly manifest: CatalogManifest,
    private base: string,
    nodes: CatalogNode[][],
  ) {
    nodes.forEach((group, part) =>
      group.forEach((node) => {
        if (this.routes.has(node.id)) throw new Error(`Duplicate catalog object: ${node.id}`)
        this.routes.set(node.id, part)
      }),
    )
    this.index = buildIndex({
      ...manifest,
      nodes: nodes.flat(),
      edges: [],
      recentChanges: manifest.recentChanges ?? [],
      coChangePairs: manifest.coChangePairs ?? [],
      orphanedReferences: manifest.orphanedReferenceCount === undefined ? undefined : [],
      systemReferences: [],
      linkedServerReferences: [],
    })
    this.index.source = this
  }

  private async read<T>(file: string, signal?: AbortSignal): Promise<T> {
    return readPartition<T>(this.base, file, signal)
  }

  async object(id: string): Promise<CatalogNode | undefined> {
    const part = this.routes.get(id)
    if (part === undefined) return undefined
    let request = this.details.get(part)
    if (request) this.details.delete(part)
    else {
      const partition = this.manifest.partitions[part]
      request = Promise.all([
        this.read<Omit<CatalogNode, 'ddl' | 'sections' | 'grants'>[]>(partition.details),
        this.read<SearchRecord[]>(partition.search),
        this.read<{ id: string; grants: CatalogGrant[] }[]>(partition.grants),
      ]).then(([details, search, permissions]) => {
        const sql = new Map(search.map((record) => [record.id, record]))
        const grants = new Map(permissions.map((record) => [record.id, record.grants]))
        return details.map((detail) => {
          const record = sql.get(detail.id)
          if (!record) throw new Error(`Object is missing from its search partition: ${detail.id}`)
          if ((this.index.byId.get(detail.id)?.grantCount ?? 0) > 0 && !grants.has(detail.id))
            throw new Error(`Object is missing from its permission partition: ${detail.id}`)
          return { ...detail, ...record, grants: grants.get(detail.id) ?? [] }
        })
      })
      request.catch(() => {
        if (this.details.get(part) === request) this.details.delete(part)
      })
    }
    this.details.set(part, request)
    // Keep repeated navigation quick without retaining every DDL/history payload.
    while (this.details.size > 8) this.details.delete(this.details.keys().next().value!)
    const node = (await request).find((candidate) => candidate.id === id)
    if (!node) throw new Error(`Object is missing from its detail partition: ${id}`)
    return node
  }

  private async edgePartition(part: number): Promise<void> {
    if (this.loadedEdges.has(part)) return
    const existing = this.edgeRequests.get(part)
    if (existing) return existing
    const request = (async () => {
      const payload = await this.read<EdgePayload>(this.manifest.partitions[part].edges)
      for (const edge of payload.edges) this.addEdge(edge)
      for (const field of ['orphanedReferences', 'systemReferences', 'linkedServerReferences'] as const) {
        for (const ref of payload[field] ?? []) {
          const key = field + JSON.stringify(ref)
          if (this.referenceKeys.has(key)) continue
          this.referenceKeys.add(key)
          if (field === 'linkedServerReferences' && 'linkedServer' in ref) {
            this.index.catalog.linkedServerReferences!.push(ref)
            append(this.index.linkedServerRefsByFrom, ref.from, ref)
            append(this.index.linkedServerRefsByLink, ref.linkedServer, ref)
          } else if (field === 'orphanedReferences') {
            this.index.catalog.orphanedReferences!.push(ref)
            append(this.index.orphanedByFrom, ref.from, ref)
          } else if (field === 'systemReferences') {
            this.index.catalog.systemReferences!.push(ref)
            append(this.index.systemRefsByFrom, ref.from, ref)
          }
        }
      }
      this.loadedEdges.add(part)
    })()
    this.edgeRequests.set(part, request)
    try {
      await request
    } finally {
      this.edgeRequests.delete(part)
    }
  }

  private addEdge(edge: CatalogEdge) {
    const key = JSON.stringify([edge.from, edge.to])
    if (this.edgeKeys.has(key)) return
    this.edgeKeys.add(key)
    this.index.catalog.edges.push(edge)
    append(this.index.outgoing, edge.from, edge.to)
    append(this.index.incoming, edge.to, edge.from)
    if (edge.columns?.length) this.index.edgeColumns.set(`${edge.from}|${edge.to}`, edge.columns)
    if (edge.dynamic) this.index.dynamicEdges.add(`${edge.from}|${edge.to}`)
  }

  async edges(ids?: string[], signal?: AbortSignal): Promise<CatalogIndex> {
    const parts = ids
      ? [...new Set(ids.map((id) => this.routes.get(id)).filter((part): part is number => part !== undefined))]
      : this.manifest.partitions.map((_, part) => part)
    await parallel(parts, async (part) => {
      checkAbort(signal)
      await this.edgePartition(part)
    })
    checkAbort(signal)
    return { ...this.index }
  }

  async neighborhood(
    id: string,
    dependencies: number,
    dependents: number,
    signal?: AbortSignal,
  ): Promise<CatalogIndex> {
    if (!this.routes.has(id)) return { ...this.index }
    await this.edges([id], signal)
    // Expand through the production traversal, including attributed remote hops.
    // Loading both endpoints' incident edges preserves incoming cross-partition links.
    while (true) {
      checkAbort(signal)
      const ids = getDirectionalNeighborhood(this.index, id, dependencies, dependents)
      if (ids.every((nodeId) => this.loadedEdges.has(this.routes.get(nodeId)!))) return { ...this.index }
      await this.edges(ids, signal)
    }
  }

  async alerts(signal?: AbortSignal): Promise<CatalogIndex> {
    const parts = this.manifest.partitions
      .map((part, index) => ({ part, index }))
      .filter(({ part }) => (part.orphanedReferenceCount ?? 0) > 0)
    await parallel(parts, async ({ index }) => {
      checkAbort(signal)
      await this.edgePartition(index)
    })
    checkAbort(signal)
    return { ...this.index }
  }

  async permissions(ids: string[], signal?: AbortSignal): Promise<Map<string, CatalogGrant[]>> {
    const wanted = new Set(ids)
    const parts = [
      ...new Set(ids.map((id) => this.routes.get(id)).filter((part): part is number => part !== undefined)),
    ]
    const result = new Map<string, CatalogGrant[]>()
    await parallel(parts, async (part) => {
      checkAbort(signal)
      const records = await this.read<{ id: string; grants: CatalogGrant[] }[]>(
        this.manifest.partitions[part].grants,
        signal,
      )
      for (const record of records) if (wanted.has(record.id)) result.set(record.id, record.grants)
    })
    checkAbort(signal)
    for (const id of ids)
      if ((this.index.byId.get(id)?.grantCount ?? 0) > 0 && !result.has(id))
        throw new Error(`Object is missing from its permission partition: ${id}`)
    return result
  }

  async search(nodes: CatalogNode[], tokens: FilterToken[], signal: AbortSignal): Promise<CatalogNode[]> {
    const contentTokens = tokens.filter((token) => token.attribute === null || token.attribute === 'ddl')
    const candidates = applyFilters(
      nodes,
      tokens.filter((token) => token.attribute !== null && token.attribute !== 'ddl'),
    )
    if (!contentTokens.length) return candidates
    const groups = new Map<number, CatalogNode[]>()
    for (const node of candidates) append(groups, this.routes.get(node.id)!, node)
    const matches = new Set<string>()
    // Four bounded lanes overlap network requests. Each worker retains one search
    // shard; full detail/history objects are never hydrated by a text search.
    const entries = [...groups]
    let next = 0
    const controller = new AbortController()
    const abort = () => controller.abort()
    signal.addEventListener('abort', abort, { once: true })
    const workers: Worker[] = []
    const run = async () => {
      const worker =
        typeof Worker === 'undefined'
          ? undefined
          : new Worker(new URL('./catalogSearch.worker.ts', import.meta.url), { type: 'module' })
      if (worker) workers.push(worker)
      while (next < entries.length) {
        checkAbort(signal)
        checkAbort(controller.signal)
        const [part, group] = entries[next++]
        const records = await this.read<SearchRecord[]>(this.manifest.partitions[part].search, controller.signal)
        checkAbort(controller.signal)
        const byId = new Map(records.map((record) => [record.id, record]))
        const searchNodes = group.map((node) => {
          const record = byId.get(node.id)
          if (!record) throw new Error(`Object is missing from its search partition: ${node.id}`)
          return { ...node, ...record }
        })
        const ids = worker
          ? await new Promise<string[]>((resolve, reject) => {
              const cancelled = () => reject(new DOMException('Cancelled', 'AbortError'))
              controller.signal.addEventListener('abort', cancelled, { once: true })
              worker.onmessage = (event: MessageEvent<string[]>) => {
                controller.signal.removeEventListener('abort', cancelled)
                resolve(event.data)
              }
              worker.onerror = () => {
                controller.signal.removeEventListener('abort', cancelled)
                reject(new Error('Catalog search failed'))
              }
              worker.postMessage({ nodes: searchNodes, tokens: contentTokens })
            })
          : applyFilters(searchNodes, contentTokens).map((node) => node.id)
        ids.forEach((id) => matches.add(id))
      }
    }
    try {
      await Promise.all(Array.from({ length: Math.min(4, entries.length) }, run))
      checkAbort(signal)
      return candidates.filter((node) => matches.has(node.id))
    } finally {
      controller.abort()
      signal.removeEventListener('abort', abort)
      workers.forEach((worker) => worker.terminate())
    }
  }
}

function append<K, T>(map: Map<K, T[]>, key: K, value: T) {
  const values = map.get(key)
  if (values) values.push(value)
  else map.set(key, [value])
}

export async function loadPartitionedCatalog(manifest: CatalogManifest, base: string): Promise<CatalogIndex> {
  if (manifest.format !== 'syncsql-partitioned' || manifest.version !== 1)
    throw new Error('Unsupported catalog format/version')
  const groups: CatalogNode[][] = new Array(manifest.partitions.length)
  await parallel(manifest.summaries, async (summary) => {
    const page = await readPartition<{ partition: number; nodes: CatalogNode[] }[]>(base, summary.file)
    if (page.reduce((count, group) => count + group.nodes.length, 0) !== summary.count)
      throw new Error('Catalog summary count does not match its manifest')
    for (const { partition, nodes } of page) {
      if (!manifest.partitions[partition] || groups[partition] || nodes.length !== manifest.partitions[partition].count)
        throw new Error('Invalid summary partition assignment')
      groups[partition] = nodes.map((node) => ({
        ...node,
        ddl: '',
        sections: [],
        history: [],
        columns: node.columns ?? [],
        grants: node.grants ?? [],
        metrics: node.metrics ?? [],
      }))
    }
  })
  if (manifest.partitions.some((_, part) => !groups[part])) throw new Error('Missing catalog summary partition')
  if (groups.reduce((count, nodes) => count + nodes.length, 0) !== manifest.nodeCount)
    throw new Error('Catalog object count does not match its manifest')
  return new PartitionedCatalog(manifest, base, groups).index
}
