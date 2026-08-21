export interface CatalogColumn {
  name: string
  description: string | null
  /** Present when the extraction backend attached a full structural column list (Tables/Views); null otherwise. */
  dataType: string | null
}

export interface CatalogGrant {
  permission: string
  /** 'GRANT' or 'DENY' (MSSQL only supports DENY; Oracle grants are always 'GRANT'). */
  state: string
  grantee: string
  /** e.g. SQL_USER, WINDOWS_USER, DATABASE_ROLE (MSSQL); null when unknown (Oracle). */
  granteeType: string | null
  /** Set when the grant was scoped to a single column rather than the whole object. */
  column: string | null
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
  grants: CatalogGrant[]
  sections: CatalogSection[]
  sizeBytes: number
  changeCount: number
  lastChangedAt: string | null
  history: CatalogObjectVersion[]
}

export interface CatalogEdge {
  from: string
  to: string
  /** Best-effort: names of `to`'s columns detected as referenced from `from`'s DDL. */
  columns: string[]
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
