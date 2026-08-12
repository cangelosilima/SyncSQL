import type { Catalog, CatalogNode } from '../types'

export interface CatalogIndex {
  catalog: Catalog
  byId: Map<string, CatalogNode>
  outgoing: Map<string, string[]>
  incoming: Map<string, string[]>
  tree: TreeServer[]
}

export interface TreeServer {
  name: string
  databases: TreeDatabase[]
}

export interface TreeDatabase {
  name: string
  server: string
  types: TreeType[]
}

export interface TreeType {
  name: string
  server: string
  database: string
  schemas: TreeSchema[]
  looseNodes: CatalogNode[]
}

export interface TreeSchema {
  name: string
  nodes: CatalogNode[]
}

export async function loadCatalog(): Promise<CatalogIndex> {
  const res = await fetch(`${import.meta.env.BASE_URL}data/catalog.json`)
  if (!res.ok) {
    throw new Error(`Failed to load catalog.json: ${res.status} ${res.statusText}`)
  }
  const catalog = (await res.json()) as Catalog
  return buildIndex(catalog)
}

export function buildIndex(catalog: Catalog): CatalogIndex {
  const byId = new Map<string, CatalogNode>()
  for (const node of catalog.nodes) byId.set(node.id, node)

  const outgoing = new Map<string, string[]>()
  const incoming = new Map<string, string[]>()
  for (const edge of catalog.edges) {
    if (!outgoing.has(edge.from)) outgoing.set(edge.from, [])
    outgoing.get(edge.from)!.push(edge.to)
    if (!incoming.has(edge.to)) incoming.set(edge.to, [])
    incoming.get(edge.to)!.push(edge.from)
  }

  const tree = buildTree(catalog.nodes)

  return { catalog, byId, outgoing, incoming, tree }
}

function buildTree(nodes: CatalogNode[]): TreeServer[] {
  const servers = new Map<string, Map<string, Map<string, { schemas: Map<string, CatalogNode[]>; loose: CatalogNode[] }>>>()

  for (const node of nodes) {
    if (!servers.has(node.server)) servers.set(node.server, new Map())
    const databases = servers.get(node.server)!

    if (!databases.has(node.database)) databases.set(node.database, new Map())
    const types = databases.get(node.database)!

    if (!types.has(node.type)) types.set(node.type, { schemas: new Map(), loose: [] })
    const typeBucket = types.get(node.type)!

    if (node.schema) {
      if (!typeBucket.schemas.has(node.schema)) typeBucket.schemas.set(node.schema, [])
      typeBucket.schemas.get(node.schema)!.push(node)
    } else {
      typeBucket.loose.push(node)
    }
  }

  const result: TreeServer[] = []
  for (const [serverName, databases] of [...servers.entries()].sort(([a], [b]) => a.localeCompare(b))) {
    const dbList: TreeDatabase[] = []
    for (const [dbName, types] of [...databases.entries()].sort(([a], [b]) => a.localeCompare(b))) {
      const typeList: TreeType[] = []
      for (const [typeName, bucket] of [...types.entries()].sort(([a], [b]) => a.localeCompare(b))) {
        const schemaList: TreeSchema[] = [...bucket.schemas.entries()]
          .sort(([a], [b]) => a.localeCompare(b))
          .map(([schemaName, schemaNodes]) => ({
            name: schemaName,
            nodes: schemaNodes.sort((a, b) => a.name.localeCompare(b.name)),
          }))
        typeList.push({
          name: typeName,
          server: serverName,
          database: dbName,
          schemas: schemaList,
          looseNodes: bucket.loose.sort((a, b) => a.name.localeCompare(b.name)),
        })
      }
      dbList.push({ name: dbName, server: serverName, types: typeList })
    }
    result.push({ name: serverName, databases: dbList })
  }
  return result
}

export function matchesQuery(node: CatalogNode, query: string): boolean {
  if (!query) return true
  const needle = query.toLowerCase()
  return (
    node.name.toLowerCase().includes(needle) ||
    node.qualifiedName.toLowerCase().includes(needle) ||
    node.database.toLowerCase().includes(needle) ||
    node.server.toLowerCase().includes(needle) ||
    (node.description ?? '').toLowerCase().includes(needle)
  )
}
