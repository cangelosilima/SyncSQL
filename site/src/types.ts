export interface CatalogColumn {
  name: string
  description: string | null
}

export interface CatalogSection {
  title: string
  content: string
}

export interface CatalogObjectVersion {
  sha: string
  date: string
  message: string
  ddl: string | null
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
  sections: CatalogSection[]
  sizeBytes: number
  changeCount: number
  lastChangedAt: string | null
  history: CatalogObjectVersion[]
}

export interface CatalogEdge {
  from: string
  to: string
}

export interface CatalogCommit {
  sha: string
  date: string
  message: string
  objectIds: string[]
}

export interface CoChangePair {
  a: string
  b: string
  count: number
}

export interface Catalog {
  generatedAt: string
  servers: string[]
  typeCounts: Record<string, number>
  nodes: CatalogNode[]
  edges: CatalogEdge[]
  recentChanges: CatalogCommit[]
  coChangePairs: CoChangePair[]
}
