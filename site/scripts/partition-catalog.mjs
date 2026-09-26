import { createReadStream, closeSync, openSync, writeSync } from 'node:fs'
import { copyFile, mkdir, mkdtemp, readFile, readdir, rename, rm, writeFile } from 'node:fs/promises'
import { createHash } from 'node:crypto'
import { gzipSync } from 'node:zlib'
import { createInterface } from 'node:readline'
import { compose } from 'node:stream'
import path from 'node:path'
import { fileURLToPath } from 'node:url'
import { parser } from 'stream-json'
import { pick } from 'stream-json/filters/pick.js'
import { ignore } from 'stream-json/filters/ignore.js'
import { streamArray } from 'stream-json/streamers/stream-array.js'
import { streamObject } from 'stream-json/streamers/stream-object.js'

const hash = (value) => createHash('sha256').update(value).digest('hex')
const partPath = /^_catalog\/[a-f0-9]{64}\.json(?:\.gz)?$/

async function* items(input, field) {
  const stream = compose(
    createReadStream(input),
    parser.asStream({ streamValues: false }),
    pick.asStream({ filter: field }),
    streamArray.asStream(),
  )
  for await (const { value } of stream) yield value
}

async function metadata(input) {
  const result = {}
  const stream = compose(
    createReadStream(input),
    parser.asStream({ streamValues: false }),
    ignore.asStream({ filter: /^(nodes|edges)$/ }),
    streamObject.asStream(),
  )
  for await (const { key, value } of stream) result[key] = value
  return result
}

// Bounded file-descriptor cache for spooling arbitrarily many databases/chunks.
class Spool {
  files = new Map()
  append(file, value) {
    let fd = this.files.get(file)
    if (fd !== undefined) this.files.delete(file)
    else fd = openSync(file, 'a')
    this.files.set(file, fd)
    writeSync(fd, JSON.stringify(value) + '\n')
    if (this.files.size > 32) {
      const [oldFile, oldFd] = this.files.entries().next().value
      closeSync(oldFd)
      this.files.delete(oldFile)
    }
  }
  close() {
    for (const fd of this.files.values()) closeSync(fd)
    this.files.clear()
  }
}

async function* lines(file) {
  const reader = createInterface({ input: createReadStream(file), crlfDelay: Infinity })
  // Spool.append writes exactly one JSON value per line.
  for await (const line of reader) yield JSON.parse(line)
}

/** Export a static snapshot without ever parsing the full nodes/edges arrays. */
export async function partitionCatalog(
  input,
  output,
  { maxNodes = 1024, maxBytes = 2 * 1024 * 1024, prune = false } = {},
) {
  if (!Number.isInteger(maxNodes) || maxNodes < 1 || !Number.isInteger(maxBytes) || maxBytes < 1)
    throw new Error('Invalid partition limits')
  input = path.resolve(input)
  output = path.resolve(output)
  await mkdir(path.join(output, '_catalog'), { recursive: true })
  const meta = await metadata(input)
  const publishManifest = async (manifest) => {
    const temporary = path.join(output, 'catalog.json.tmp')
    await writeFile(temporary, JSON.stringify(manifest))
    await rename(temporary, path.join(output, 'catalog.json'))
    if (prune) {
      const active = new Set([
        ...manifest.summaries.map((summary) => summary.file),
        ...manifest.partitions.flatMap((part) => [part.details, part.edges, part.search, part.grants]),
      ])
      for (const file of await readdir(path.join(output, '_catalog'))) {
        const relative = `_catalog/${file}`
        if (partPath.test(relative) && !active.has(relative)) await rm(path.join(output, relative))
      }
    }
    return manifest
  }
  if (meta.format !== undefined) {
    if (meta.format !== 'syncsql-partitioned' || meta.version !== 1)
      throw new Error('Unsupported catalog format/version')
    const files = [
      ...meta.summaries.map((summary) => summary.file),
      ...meta.partitions.flatMap((partition) =>
        ['details', 'edges', 'search', 'grants'].map((field) => partition[field]),
      ),
    ]
    for (const relative of files) {
      if (!partPath.test(relative)) throw new Error(`Invalid partition path: ${relative}`)
      const source = path.resolve(path.dirname(input), relative)
      const target = path.resolve(output, relative)
      if (source !== target) await copyFile(source, target)
    }
    return publishManifest(meta)
  }

  const temporary = await mkdtemp(path.join(output, '.partition-'))
  const spool = new Spool()
  const groups = new Map()
  const route = new Map()
  const partOf = []
  const serverOf = []
  const engineOf = []
  const tableIds = []
  const typeCounts = {}
  const partitions = []
  const writePart = async (value) => {
    const json = JSON.stringify(value)
    const relative = `_catalog/${hash(json)}.json.gz`
    await writeFile(path.join(output, relative), gzipSync(json))
    return relative
  }
  try {
    for await (const node of items(input, 'nodes')) {
      if (!node.id || route.has(node.id)) throw new Error(`Missing or duplicate object id: ${node.id}`)
      route.set(node.id, route.size)
      serverOf.push(node.server)
      engineOf.push(node.engine)
      if (node.type === 'Tables') tableIds.push(node.id)
      typeCounts[node.type] = (typeCounts[node.type] ?? 0) + 1
      const key = JSON.stringify([node.server, node.database])
      if (!groups.has(key))
        groups.set(key, {
          server: node.server,
          database: node.database,
          file: path.join(temporary, `nodes-${groups.size}`),
        })
      spool.append(groups.get(key).file, node)
    }
    spool.close()
    const summaryGroups = new Map()
    for (const group of groups.values()) {
      let nodes = []
      let bytes = 0
      const flush = async () => {
        const part = partitions.length
        for (const node of nodes) partOf[route.get(node.id)] = part
        const summaries = nodes.map(
          ({ ddl: _ddl, history: _history, sections: _sections, metrics, columns, grants, ...summary }) => ({
            ...summary,
            columnNames: (columns ?? []).map((column) => column.name),
            columnCount: columns?.length ?? 0,
            grantCount: grants?.length ?? 0,
            granteeNames: [...new Set((grants ?? []).map((grant) => grant.grantee))],
            // Overview alerts need only the last two row counts and index fragmentation.
            metrics: (metrics?.length >= 2 ? metrics.slice(-2) : []).map((m) => ({
              capturedAt: m.capturedAt,
              rowCount: m.rowCount,
              reservedKB: null,
              dataKB: null,
              indexKB: null,
              indexes: (m.indexes ?? []).map((i) => ({ name: i.name, fragmentationPct: i.fragmentationPct })),
              statistics: [],
            })),
          }),
        )
        await writeFile(path.join(temporary, `summary-${part}`), JSON.stringify(summaries))
        await writeFile(path.join(temporary, `edges-${part}`), '')
        partitions.push({
          server: group.server,
          database: group.database,
          count: nodes.length,
          details: await writePart(
            nodes.map(({ ddl: _ddl, sections: _sections, grants: _grants, ...detail }) => detail),
          ),
          search: await writePart(nodes.map((n) => ({ id: n.id, ddl: n.ddl, sections: n.sections ?? [] }))),
          grants: await writePart(nodes.filter((n) => n.grants?.length).map((n) => ({ id: n.id, grants: n.grants }))),
        })
        nodes = []
        bytes = 0
      }
      for await (const node of lines(group.file)) {
        const size = Buffer.byteLength(JSON.stringify(node))
        if (nodes.length && (nodes.length >= maxNodes || bytes + size > maxBytes)) await flush()
        nodes.push(node)
        bytes += size
      }
      await flush()
    }
    const incoming = new Uint32Array(route.size)
    const outgoing = new Uint32Array(route.size)
    const predecessors = Array.from({ length: route.size }, () => [])
    let edgeCount = 0
    const add = (field, value, ids) => {
      const parts = new Set(ids.filter((id) => route.has(id)).map((id) => partOf[route.get(id)]))
      for (const part of parts) spool.append(path.join(temporary, `edges-${part}`), { field, value })
    }
    for await (const edge of items(input, 'edges')) {
      if (!route.has(edge.from) || !route.has(edge.to))
        throw new Error(`Edge refers to an unknown object: ${edge.from} -> ${edge.to}`)
      outgoing[route.get(edge.from)]++
      incoming[route.get(edge.to)]++
      predecessors[route.get(edge.to)].push(route.get(edge.from))
      edgeCount++
      add('edges', edge, [edge.from, edge.to])
    }
    for (const ref of meta.orphanedReferences ?? []) add('orphanedReferences', ref, [ref.from])
    for (const ref of meta.systemReferences ?? []) add('systemReferences', ref, [ref.from])
    for (const ref of meta.linkedServerReferences ?? [])
      add('linkedServerReferences', ref, [ref.from, ref.to, ref.linkedServer])
    const targetEngines = new Map()
    for (const ref of meta.linkedServerReferences ?? []) {
      const engine = engineOf[route.get(ref.to)]
      if (engine) {
        if (!targetEngines.has(ref.linkedServer)) targetEngines.set(ref.linkedServer, new Set())
        targetEngines.get(ref.linkedServer).add(engine)
      }
    }
    spool.close()
    for (let part = 0; part < partitions.length; part++) {
      const summaries = JSON.parse(await readFile(path.join(temporary, `summary-${part}`), 'utf8'))
      for (const summary of summaries) {
        summary.dependsOnCount = outgoing[route.get(summary.id)]
        summary.usedByCount = incoming[route.get(summary.id)]
        if (targetEngines.has(summary.id)) summary.linkTargetEngines = [...targetEngines.get(summary.id)].sort()
      }
      const scope = JSON.stringify([partitions[part].server, partitions[part].database])
      if (!summaryGroups.has(scope)) summaryGroups.set(scope, [])
      summaryGroups.get(scope).push({ partition: part, nodes: summaries })
      const payload = { edges: [], orphanedReferences: [], systemReferences: [], linkedServerReferences: [] }
      // Every partition has an edge spool, including partitions with no edges.
      // A missing spool is a publication failure, not an empty relationship set.
      for await (const { field, value } of lines(path.join(temporary, `edges-${part}`))) payload[field].push(value)
      partitions[part].edges = await writePart(payload)
      partitions[part].orphanedReferenceCount = payload.orphanedReferences.length
    }
    const summaries = []
    for (const groups of summaryGroups.values()) {
      let page = []
      let bytes = 0
      const flush = async () => {
        summaries.push({
          file: await writePart(page),
          count: page.reduce((count, group) => count + group.nodes.length, 0),
        })
        page = []
        bytes = 0
      }
      for (const group of groups) {
        const size = Buffer.byteLength(JSON.stringify(group))
        if (page.length && bytes + size > maxBytes) await flush()
        page.push(group)
        bytes += size
      }
      await flush()
    }
    const { orphanedReferences, systemReferences: _system, linkedServerReferences: _linked, ...header } = meta
    // Match the overview's six-hop, stop-after-crossing-server reachability.
    // Only direct-count contenders need traversal, including ties at rank ten.
    const ranked = tableIds
      .filter((id) => incoming[route.get(id)] > 0)
      .sort((a, b) => incoming[route.get(b)] - incoming[route.get(a)])
    const threshold = ranked.length ? incoming[route.get(ranked[Math.min(9, ranked.length - 1)])] : 0
    const topReferencedTables = ranked
      .filter((id) => incoming[route.get(id)] >= threshold)
      .map((id) => {
        const root = route.get(id)
        const visited = new Set([root])
        let frontier = [root]
        for (let hop = 0; hop < 6 && frontier.length; hop++) {
          const next = []
          for (const current of frontier)
            for (const previous of predecessors[current]) {
              if (visited.has(previous)) continue
              visited.add(previous)
              if (serverOf[current] === serverOf[previous]) next.push(previous)
            }
          frontier = next
        }
        return { id, directUsers: incoming[root], indirectUsers: visited.size - 1 }
      })
      .sort((a, b) => b.directUsers - a.directUsers || b.indirectUsers - a.indirectUsers)
      .slice(0, 10)
    return await publishManifest({
      ...header,
      typeCounts,
      format: 'syncsql-partitioned',
      version: 1,
      nodeCount: route.size,
      edgeCount,
      topReferencedTables,
      orphanedReferenceCount: orphanedReferences?.length,
      summaries,
      partitions,
    })
  } finally {
    spool.close()
    // Only our mkdtemp directory is removed, never the caller's output directory.
    await rm(temporary, { recursive: true, force: true })
  }
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const args = process.argv.slice(2)
  const option = (name, fallback) => (args.includes(name) ? args[args.indexOf(name) + 1] : fallback)
  const input = option('--input', 'public/data/catalog.json')
  const output = option('--output', 'dist/data')
  const manifest = await partitionCatalog(input, output, { prune: args.includes('--prune') })
  console.log(
    `Published ${manifest.nodeCount} objects and ${manifest.edgeCount} edges in ${manifest.partitions.length} static partitions: ${output}`,
  )
}
