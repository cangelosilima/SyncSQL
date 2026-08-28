import type { CatalogIndex } from './catalog'
import type { CatalogNode, CoChangePair } from '../types'
import { getReachable } from './reachability'

export interface RankedNode {
  node: CatalogNode
  value: number
}

export function getRecentlyChanged(index: CatalogIndex, limit = 10): RankedNode[] {
  return index.catalog.nodes
    .filter((n) => n.lastChangedAt)
    .sort((a, b) => (b.lastChangedAt! < a.lastChangedAt! ? -1 : b.lastChangedAt! > a.lastChangedAt! ? 1 : 0))
    .slice(0, limit)
    .map((node) => ({ node, value: 0 }))
}

export function getMostChanged(index: CatalogIndex, limit = 10): RankedNode[] {
  return index.catalog.nodes
    .filter((n) => n.changeCount > 0)
    .sort((a, b) => b.changeCount - a.changeCount)
    .slice(0, limit)
    .map((node) => ({ node, value: node.changeCount }))
}

export interface TableUsage {
  node: CatalogNode
  directUsers: number
  indirectUsers: number
}

/**
 * Tables ranked by how many other objects depend on them - directly (an
 * immediate incoming edge) and indirectly (full reachable incoming set,
 * subject to the same cross-linked-server one-hop cutoff as the lineage
 * graph - see lib/reachability.ts).
 */
export function getTopReferencedTables(index: CatalogIndex, limit = 10): TableUsage[] {
  const tableTypes = new Set(['Tables'])
  const usages: TableUsage[] = index.catalog.nodes
    .filter((n) => tableTypes.has(n.type))
    .map((node) => ({
      node,
      directUsers: (index.incoming.get(node.id) ?? []).length,
      indirectUsers: getReachable(node.id, 'incoming', index).size,
    }))
    .filter((u) => u.directUsers > 0)

  return usages.sort((a, b) => b.directUsers - a.directUsers || b.indirectUsers - a.indirectUsers).slice(0, limit)
}

export interface CoChangeDisplay extends CoChangePair {
  nodeA: CatalogNode | undefined
  nodeB: CatalogNode | undefined
}

export function getCoChangePairs(index: CatalogIndex, limit = 10): CoChangeDisplay[] {
  return index.catalog.coChangePairs.slice(0, limit).map((pair) => ({
    ...pair,
    nodeA: index.byId.get(pair.a),
    nodeB: index.byId.get(pair.b),
  }))
}

export function intensity(value: number, max: number): number {
  if (max <= 0) return 0
  return Math.max(0.08, Math.min(1, value / max))
}

/** Compact "Nd ago" / "Nh ago" style relative time for status-line and stat-card display. */
export function formatRelative(dateStr: string): string {
  const then = new Date(dateStr).getTime()
  if (Number.isNaN(then)) return dateStr
  const diffMs = Date.now() - then
  const minutes = Math.round(diffMs / 60000)
  if (minutes < 1) return 'just now'
  if (minutes < 60) return `${minutes}m ago`
  const hours = Math.round(minutes / 60)
  if (hours < 24) return `${hours}h ago`
  const days = Math.round(hours / 24)
  if (days < 30) return `${days}d ago`
  const months = Math.round(days / 30)
  if (months < 12) return `${months}mo ago`
  return `${Math.round(months / 12)}y ago`
}
