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
  /** Engine of the owning server; absent in older catalog snapshots. */
  engine?: string | null
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
  /**
   * True when only SQL built as a string at runtime (an `OPENQUERY` body, an
   * `EXEC` of a literal, a variable assembled then executed) produced this edge -
   * a weaker signal than one read off the parse tree. Absent on catalogs built
   * before this field existed; an edge that any static reference also backs is
   * never marked.
   */
  dynamic?: boolean
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

/**
 * A reference in `from`'s DDL that couldn't be resolved to anything in the
 * current catalog's scope (same server+database, or bare on the same
 * server) - almost always a real bug (renamed/dropped target), occasionally
 * a false positive (dynamic SQL, a genuinely external object). See
 * README's "Orphaned reference detection".
 */
export interface CatalogOrphanedReference {
  /** Node id of the object whose DDL contains the unresolved reference. */
  from: string
  /** The linked server / database link the reference named, when it named one. */
  server?: string | null
  /** The database the reference named, when it named one. */
  database?: string | null
  schema: string | null
  name: string
}

/**
 * A reference to something the database engine provides rather than something anybody
 * extracted - `sp_executesql`, `sys.objects`, `DBMS_OUTPUT`. It resolves to nothing in
 * the catalog, but it is not missing and never was, so it is listed separately instead
 * of being counted as an orphaned reference. See README's "Orphaned reference detection".
 */
export interface CatalogSystemReference {
  /** Node id of the object whose DDL makes the reference. */
  from: string
  server?: string | null
  database?: string | null
  schema: string | null
  name: string
}

/**
 * One reference that crosses a linked server / database link: who makes it, which link it crosses, the
 * target as the DDL writes it, and the target node when the catalog has it extracted (null when the hop
 * lands outside the catalog's scope).
 */
export interface CatalogLinkedServerReference {
  /** Node id of the LinkedServers/DatabaseLinks object the reference crosses. */
  linkedServer: string
  /** Node id of the object whose DDL makes the reference. */
  from: string
  /** Node id of the referenced object, or null when it isn't in the catalog. */
  to: string | null
  database?: string | null
  schema: string | null
  name: string
  /** True when only dynamically-built SQL made this reference (see `CatalogEdge.dynamic`). */
  dynamic?: boolean
}

export interface Catalog {
  /** Present only for the explicitly published synthetic benchmark snapshot. */
  example?: {
    scenario: string
    title: string
    gatewayVerified: boolean
    users: { name: string; scope: string; server: string; database: string }[]
    paths: { name: string; nodes: string[] }[]
  }
  generatedAt: string
  servers: string[]
  typeCounts: Record<string, number>
  nodes: CatalogNode[]
  edges: CatalogEdge[]
  recentChanges: CatalogCommit[]
  coChangePairs: CoChangePair[]
  /** Absent on catalogs built before this field existed - treat as empty. */
  orphanedReferences?: CatalogOrphanedReference[]
  systemReferences?: CatalogSystemReference[]
  linkedServerReferences?: CatalogLinkedServerReference[]
}
