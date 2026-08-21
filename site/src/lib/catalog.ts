import type { Catalog, CatalogNode } from '../types'

export interface CatalogIndex {
  catalog: Catalog
  byId: Map<string, CatalogNode>
  outgoing: Map<string, string[]>
  incoming: Map<string, string[]>
  /** "from|to" -> the target's columns detected as referenced by the source (see CatalogEdge.columns). */
  edgeColumns: Map<string, string[]>
  tree: TreeServer[]
}

export interface TreeServer {
  name: string
  databases: TreeDatabase[]
}

export interface TreeDatabase {
  name: string
  server: string
  schemas: TreeSchema[]
}

export interface TreeSchema {
  /** Display name; NO_SCHEMA_LABEL for schema-less types (Schemas, LinkedServers, Replication, ...). */
  name: string
  server: string
  database: string
  types: TreeType[]
}

export interface TreeType {
  name: string
  nodes: CatalogNode[]
}

export const NO_SCHEMA_LABEL = '(server-level)'

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
  const edgeColumns = new Map<string, string[]>()
  for (const edge of catalog.edges) {
    if (!outgoing.has(edge.from)) outgoing.set(edge.from, [])
    outgoing.get(edge.from)!.push(edge.to)
    if (!incoming.has(edge.to)) incoming.set(edge.to, [])
    incoming.get(edge.to)!.push(edge.from)
    if (edge.columns && edge.columns.length > 0) edgeColumns.set(`${edge.from}|${edge.to}`, edge.columns)
  }

  const tree = buildTree(catalog.nodes)

  return { catalog, byId, outgoing, incoming, edgeColumns, tree }
}

function buildTree(nodes: CatalogNode[]): TreeServer[] {
  const servers = new Map<string, Map<string, Map<string, Map<string, CatalogNode[]>>>>()

  for (const node of nodes) {
    const schemaKey = node.schema ?? NO_SCHEMA_LABEL

    if (!servers.has(node.server)) servers.set(node.server, new Map())
    const databases = servers.get(node.server)!

    if (!databases.has(node.database)) databases.set(node.database, new Map())
    const schemas = databases.get(node.database)!

    if (!schemas.has(schemaKey)) schemas.set(schemaKey, new Map())
    const types = schemas.get(schemaKey)!

    if (!types.has(node.type)) types.set(node.type, [])
    types.get(node.type)!.push(node)
  }

  const result: TreeServer[] = []
  for (const [serverName, databases] of [...servers.entries()].sort(([a], [b]) => a.localeCompare(b))) {
    const dbList: TreeDatabase[] = []
    for (const [dbName, schemas] of [...databases.entries()].sort(([a], [b]) => a.localeCompare(b))) {
      const schemaList: TreeSchema[] = [...schemas.entries()]
        .sort(([a], [b]) => (a === NO_SCHEMA_LABEL ? 1 : b === NO_SCHEMA_LABEL ? -1 : a.localeCompare(b)))
        .map(([schemaName, types]) => ({
          name: schemaName,
          server: serverName,
          database: dbName,
          types: [...types.entries()]
            .sort(([a], [b]) => a.localeCompare(b))
            .map(([typeName, typeNodes]) => ({
              name: typeName,
              nodes: typeNodes.sort((a, b) => a.name.localeCompare(b.name)),
            })),
        }))
      dbList.push({ name: dbName, server: serverName, schemas: schemaList })
    }
    result.push({ name: serverName, databases: dbList })
  }
  return result
}
