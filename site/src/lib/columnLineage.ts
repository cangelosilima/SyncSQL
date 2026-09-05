import type { CatalogIndex } from './catalog'
import type { CatalogNode } from '../types'

/**
 * Lineage for a single column of one object.
 *
 * Only one direction of this is real data. Every lineage edge carries the names of the
 * *target's* columns that the source was seen referencing (see CatalogEdge.columns), so
 * "who reads this column" is answerable exactly: it is the incoming edges tagged with it.
 * The other direction - what feeds this column - would need expression-level lineage
 * (which SELECT expression produced which output column), and nothing in the catalog
 * records that. Rather than dress up a same-name guess as an answer, this module only
 * reports what was actually observed.
 *
 * The signal itself stays best-effort for the reasons the object page already states: it
 * comes from qualified `alias.column` references detected in DDL, so it misses `SELECT *`,
 * computed expressions and anything built at runtime.
 */

/** Column names are matched case-insensitively: an edge records the *caller's* spelling, which need not match the column's own. */
function normalize(column: string): string {
  return column.toLowerCase()
}

/** The objects whose own DDL was seen referencing `column` of `nodeId`, in id order. */
export function getColumnConsumers(index: CatalogIndex, nodeId: string, column: string): CatalogNode[] {
  const needle = normalize(column)
  const consumers: CatalogNode[] = []

  for (const fromId of index.incoming.get(nodeId) ?? []) {
    const columns = index.edgeColumns.get(`${fromId}|${nodeId}`) ?? []
    if (!columns.some((c) => normalize(c) === needle)) continue
    const node = index.byId.get(fromId)
    if (node) consumers.push(node)
  }

  return consumers.sort((a, b) => a.qualifiedName.localeCompare(b.qualifiedName))
}

/**
 * How many objects reference each of `nodeId`'s columns, keyed by the normalized column
 * name. Built in one pass so a columns table of any size can show counts without
 * re-scanning the edges per row.
 */
export function getColumnUsageCounts(index: CatalogIndex, nodeId: string): Map<string, number> {
  const counts = new Map<string, number>()

  for (const fromId of index.incoming.get(nodeId) ?? []) {
    // One consuming object counts once per column however many times its DDL names it.
    const seen = new Set<string>()
    for (const column of index.edgeColumns.get(`${fromId}|${nodeId}`) ?? []) {
      const key = normalize(column)
      if (seen.has(key)) continue
      seen.add(key)
      counts.set(key, (counts.get(key) ?? 0) + 1)
    }
  }

  return counts
}

/** Convenience for a columns table row: how many objects read this column. */
export function getColumnUsageCount(counts: Map<string, number>, column: string): number {
  return counts.get(normalize(column)) ?? 0
}
