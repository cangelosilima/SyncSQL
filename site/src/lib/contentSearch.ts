import type { CatalogNode } from '../types'

/**
 * Full text a content search should scan for a node - the DDL plus any
 * appended sections (Foreign Keys, Check Constraints, Indexes, ...), which
 * carry real references too (e.g. a FK's REFERENCES target).
 */
export function nodeContentText(node: CatalogNode): string {
  if (node.sections.length === 0) return node.ddl
  return `${node.ddl}\n${node.sections.map((s) => s.content).join('\n')}`
}

export function matchesContentQuery(node: CatalogNode, query: string): boolean {
  const needle = query.trim().toLowerCase()
  if (!needle) return true
  return nodeContentText(node).toLowerCase().includes(needle)
}

/** Filters nodes whose DDL (+ appended sections) contains `query`, case-insensitively. Empty query is a no-op. */
export function filterByContent(nodes: CatalogNode[], query: string): CatalogNode[] {
  const needle = query.trim().toLowerCase()
  if (!needle) return nodes
  return nodes.filter((node) => nodeContentText(node).toLowerCase().includes(needle))
}
