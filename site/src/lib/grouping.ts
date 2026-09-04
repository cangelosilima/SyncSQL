import type { CatalogIndex } from './catalog'
import type { CatalogNode } from '../types'

/**
 * How a large set of related objects is broken up for display. "type" is the
 * default because it's the axis that usually explains the size - a reporting
 * view depends on 200 things because 190 of them are Tables - but a hub object
 * spanning several databases or servers reads better broken up that way.
 */
export type GroupBy = 'type' | 'database' | 'server' | 'schema'

export const GROUP_BY_OPTIONS: readonly { value: GroupBy; label: string }[] = [
  { value: 'type', label: 'Type' },
  { value: 'database', label: 'Database' },
  { value: 'schema', label: 'Schema' },
  { value: 'server', label: 'Server' },
]

export interface RelatedGroup {
  /** Stable key for this group - the raw attribute value (or NO_VALUE_LABEL when the node has none). */
  key: string
  nodes: CatalogNode[]
}

/** Stands in for a grouping attribute the node doesn't have - schema-less types (LinkedServers, Schemas, ...) grouped by schema. */
export const NO_VALUE_LABEL = '(none)'

/** Resolves node ids to catalog nodes, dropping ids the catalog doesn't have, sorted by qualified name within server/database. */
export function resolveNodes(index: CatalogIndex, ids: readonly string[]): CatalogNode[] {
  return ids
    .map((id) => index.byId.get(id))
    .filter((node): node is CatalogNode => Boolean(node))
    .sort(compareNodes)
}

/** Server, then database, then qualified name - the order someone scanning a long dependency list reads in. */
export function compareNodes(a: CatalogNode, b: CatalogNode): number {
  return (
    a.server.localeCompare(b.server) ||
    a.database.localeCompare(b.database) ||
    a.qualifiedName.localeCompare(b.qualifiedName)
  )
}

function groupValue(node: CatalogNode, by: GroupBy): string {
  switch (by) {
    case 'type':
      return node.type
    case 'database':
      return node.database
    case 'server':
      return node.server
    case 'schema':
      return node.schema ?? NO_VALUE_LABEL
  }
}

/**
 * Buckets nodes by one attribute, biggest bucket first. Biggest-first because
 * with a few hundred dependencies the useful question is "what is all this?",
 * and the answer is whichever bucket holds most of them.
 */
export function groupRelated(nodes: readonly CatalogNode[], by: GroupBy): RelatedGroup[] {
  const groups = new Map<string, CatalogNode[]>()
  for (const node of nodes) {
    const key = groupValue(node, by)
    const bucket = groups.get(key)
    if (bucket) bucket.push(node)
    else groups.set(key, [node])
  }

  return [...groups.entries()]
    .map(([key, groupNodes]) => ({ key, nodes: groupNodes }))
    .sort((a, b) => b.nodes.length - a.nodes.length || a.key.localeCompare(b.key))
}

/** Per-type counts, biggest first - the one-line "what is all this?" summary shown above a large list. */
export function countByType(nodes: readonly CatalogNode[]): { type: string; count: number }[] {
  return groupRelated(nodes, 'type').map((group) => ({ type: group.key, count: group.nodes.length }))
}

/**
 * Substring match (case-insensitive) across every part of a node's identity, so
 * "sales" narrows a 300-row list whether it names the server, the database, the
 * schema or the object itself.
 */
export function searchNodes(nodes: readonly CatalogNode[], query: string): CatalogNode[] {
  const needle = query.trim().toLowerCase()
  if (!needle) return [...nodes]
  return nodes.filter((node) =>
    [node.qualifiedName, node.name, node.schema ?? '', node.database, node.server, node.type].some((part) =>
      part.toLowerCase().includes(needle),
    ),
  )
}
