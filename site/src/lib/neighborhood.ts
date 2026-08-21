import type { CatalogIndex } from './catalog'

/**
 * BFS outward from `rootId` in both directions up to `hops` steps, returning
 * the full set of node ids to render (root included). Used by the Lineage
 * page's "drill into this object" navigation mode, where clicking a node
 * re-centers the graph on it instead of leaving the page.
 */
export function getNeighborhoodIds(index: CatalogIndex, rootId: string, hops: number): string[] {
  const seen = new Set<string>([rootId])
  let frontier = [rootId]

  for (let hop = 0; hop < hops && frontier.length > 0; hop++) {
    const next: string[] = []
    for (const id of frontier) {
      const neighbors = [...(index.outgoing.get(id) ?? []), ...(index.incoming.get(id) ?? [])]
      for (const neighborId of neighbors) {
        if (seen.has(neighborId)) continue
        seen.add(neighborId)
        next.push(neighborId)
      }
    }
    frontier = next
  }

  return [...seen]
}

/** Column names of `toId` that the edge from `fromId` is known to reference, if any. */
export function getEdgeColumns(index: CatalogIndex, fromId: string, toId: string): string[] {
  return index.edgeColumns.get(`${fromId}|${toId}`) ?? []
}
