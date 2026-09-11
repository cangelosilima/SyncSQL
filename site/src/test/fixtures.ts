import type { Catalog, CatalogEdge, CatalogNode } from '../types'

/** A CatalogNode with every required field filled in, so tests only state what they care about. */
export function makeNode(overrides: Partial<CatalogNode> & Pick<CatalogNode, 'id'>): CatalogNode {
  const server = overrides.server ?? 'SRV1'
  const database = overrides.database ?? 'AppDb'
  const schema = overrides.schema !== undefined ? overrides.schema : 'dbo'
  const name = overrides.name ?? overrides.id
  const type = overrides.type ?? 'Tables'
  return {
    id: overrides.id,
    server,
    engine: overrides.engine,
    database,
    schema,
    type,
    name,
    qualifiedName: overrides.qualifiedName ?? `${server}.${database}.${schema ?? ''}.${name}`,
    path: overrides.path ?? `${server}/${database}/${type}/${name}.sql`,
    ddl: overrides.ddl ?? `CREATE TABLE ${name} (Id INT)`,
    description: overrides.description ?? null,
    columns: overrides.columns ?? [],
    grants: overrides.grants ?? [],
    sections: overrides.sections ?? [],
    sizeBytes: overrides.sizeBytes ?? 0,
    changeCount: overrides.changeCount ?? 0,
    lastChangedAt: overrides.lastChangedAt ?? null,
    history: overrides.history ?? [],
    metrics: overrides.metrics ?? [],
  }
}

export function makeEdge(from: string, to: string, columns: string[] = []): CatalogEdge {
  return { from, to, columns }
}

export function makeCatalog(overrides: Partial<Catalog> = {}): Catalog {
  const nodes = overrides.nodes ?? []
  return {
    generatedAt: overrides.generatedAt ?? '2026-01-01T00:00:00Z',
    servers: overrides.servers ?? [...new Set(nodes.map((n) => n.server))],
    typeCounts: overrides.typeCounts ?? {},
    nodes,
    edges: overrides.edges ?? [],
    recentChanges: overrides.recentChanges ?? [],
    coChangePairs: overrides.coChangePairs ?? [],
    orphanedReferences: overrides.orphanedReferences,
    systemReferences: overrides.systemReferences,
    linkedServerReferences: overrides.linkedServerReferences,
  }
}
