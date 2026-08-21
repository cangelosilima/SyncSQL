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

export interface CatalogIndexMetric {
  name: string
  /** MSSQL: sys.dm_db_index_physical_stats / sys.dm_db_index_usage_stats. Null/absent when not available (e.g. Oracle). */
  fragmentationPct?: number | null
  pageCount?: number | null
  seeks?: number | null
  scans?: number | null
  lookups?: number | null
  updates?: number | null
  /** Oracle: ALL_IND_STATISTICS. Null/absent when not available (e.g. MSSQL). */
  rowCount?: number | null
  distinctKeys?: number | null
  leafBlocks?: number | null
  lastAnalyzed?: string | null
}

/** The statistics actually consulted by the query optimizer for cardinality estimation - not the CREATE STATISTICS object definition. */
export interface CatalogStatMetric {
  name: string
  rows: number | null
  rowsSampled: number | null
  /** Histogram step count (MSSQL only; null for Oracle). */
  steps: number | null
  modificationCounter: number | null
  lastUpdated: string | null
}

/** One run's volatile operational snapshot for a table - row counts, index metrics, optimizer statistics. Never diffed against the object's own DDL; see node.metrics. */
export interface CatalogMetricSnapshot {
  capturedAt: string
  rowCount: number | null
  reservedKB: number | null
  dataKB: number | null
  indexKB: number | null
  indexes: CatalogIndexMetric[]
  statistics: CatalogStatMetric[]
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
  /** Volatile operational history (volume, index, optimizer statistics) - oldest first, capped by the extraction pipeline's retention window. Empty for non-table objects or when unavailable. */
  metrics: CatalogMetricSnapshot[]
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
