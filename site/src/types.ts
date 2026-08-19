export interface CatalogColumn {
  name: string
  description: string | null
}

export interface CatalogNode {
  id: string
  server: string
  database: string
  schema: string | null
  type: string
  name: string
  qualifiedName: string
  path: string
  ddl: string
  description: string | null
  columns: CatalogColumn[]
  sizeBytes: number
}

export interface CatalogEdge {
  from: string
  to: string
}

export interface Catalog {
  generatedAt: string
  servers: string[]
  typeCounts: Record<string, number>
  nodes: CatalogNode[]
  edges: CatalogEdge[]
}
