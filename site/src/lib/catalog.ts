import type {
  Catalog,
  CatalogLinkedServerReference,
  CatalogNode,
  CatalogOrphanedReference,
  CatalogSystemReference,
} from '../types'

export interface CatalogIndex {
  catalog: Catalog
  byId: Map<string, CatalogNode>
  outgoing: Map<string, string[]>
  incoming: Map<string, string[]>
  /** "from|to" -> the target's columns detected as referenced by the source (see CatalogEdge.columns). */
  edgeColumns: Map<string, string[]>
  tree: TreeServer[]
  /** Node id -> orphaned references found in that node's own DDL. */
  orphanedByFrom: Map<string, CatalogOrphanedReference[]>
  /** Node id -> the engine-provided objects that node's DDL uses (sp_executesql, sys.*, DBMS_*). */
  systemRefsByFrom: Map<string, CatalogSystemReference[]>
  /** "from|to" for every edge that only dynamically-built SQL produced (see CatalogEdge.dynamic). */
  dynamicEdges: Set<string>
  /** Linked server / database link node id -> every reference the fleet makes through it. */
  linkedServerRefsByLink: Map<string, CatalogLinkedServerReference[]>
  /** Node id -> the references that object makes across a linked server / database link. */
  linkedServerRefsByFrom: Map<string, CatalogLinkedServerReference[]>
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

/** Object types whose nodes are a link to another server rather than something inside a database. */
export const LINK_TYPES = ['LinkedServers', 'DatabaseLinks']

export function isLinkNode(node: CatalogNode): boolean {
  return LINK_TYPES.includes(node.type)
}

/**
 * A reference rendered the way its DDL wrote it - "LNK.OtherDb.dbo.Orders", "OtherDb.dbo.Orders",
 * "dbo.Orders" - keeping only the parts that were actually there.
 */
export function qualifiedRefName(ref: {
  server?: string | null
  database?: string | null
  schema?: string | null
  name: string
}): string {
  return [ref.server, ref.database, ref.schema, ref.name].filter((part): part is string => !!part).join('.')
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
  const edgeColumns = new Map<string, string[]>()
  const dynamicEdges = new Set<string>()
  for (const edge of catalog.edges) {
    if (!outgoing.has(edge.from)) outgoing.set(edge.from, [])
    outgoing.get(edge.from)!.push(edge.to)
    if (!incoming.has(edge.to)) incoming.set(edge.to, [])
    incoming.get(edge.to)!.push(edge.from)
    if (edge.columns && edge.columns.length > 0) edgeColumns.set(`${edge.from}|${edge.to}`, edge.columns)
    if (edge.dynamic) dynamicEdges.add(`${edge.from}|${edge.to}`)
  }

  const tree = buildTree(catalog.nodes)

  const orphanedByFrom = new Map<string, CatalogOrphanedReference[]>()
  for (const ref of catalog.orphanedReferences ?? []) {
    if (!orphanedByFrom.has(ref.from)) orphanedByFrom.set(ref.from, [])
    orphanedByFrom.get(ref.from)!.push(ref)
  }

  const systemRefsByFrom = new Map<string, CatalogSystemReference[]>()
  for (const ref of catalog.systemReferences ?? []) {
    if (!systemRefsByFrom.has(ref.from)) systemRefsByFrom.set(ref.from, [])
    systemRefsByFrom.get(ref.from)!.push(ref)
  }

  const linkedServerRefsByLink = new Map<string, CatalogLinkedServerReference[]>()
  const linkedServerRefsByFrom = new Map<string, CatalogLinkedServerReference[]>()
  for (const ref of catalog.linkedServerReferences ?? []) {
    if (!linkedServerRefsByLink.has(ref.linkedServer)) linkedServerRefsByLink.set(ref.linkedServer, [])
    linkedServerRefsByLink.get(ref.linkedServer)!.push(ref)
    if (!linkedServerRefsByFrom.has(ref.from)) linkedServerRefsByFrom.set(ref.from, [])
    linkedServerRefsByFrom.get(ref.from)!.push(ref)
  }

  return {
    catalog,
    byId,
    outgoing,
    incoming,
    edgeColumns,
    tree,
    orphanedByFrom,
    systemRefsByFrom,
    dynamicEdges,
    linkedServerRefsByLink,
    linkedServerRefsByFrom,
  }
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
