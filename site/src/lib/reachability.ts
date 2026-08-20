import type { CatalogIndex } from './catalog'

/**
 * BFS from a seed node following outgoing/incoming edges, up to `maxHops`.
 * Whenever a hop crosses into a *different server* (typically via a linked
 * server / database link object), that neighbor is recorded as reachable
 * but traversal does not continue past it - dependency chains that hop
 * through a linked server are usually only meaningful one level deep, and
 * naively following them further risks a fan-out that doesn't reflect real
 * structure. Returns a map of reachable node id -> hop distance (seed
 * itself excluded).
 */
export function getReachable(seedId: string, direction: 'incoming' | 'outgoing', index: CatalogIndex, maxHops = 6): Map<string, number> {
  const seed = index.byId.get(seedId)
  const edgeMap = direction === 'incoming' ? index.incoming : index.outgoing
  const visited = new Map<string, number>()
  if (!seed) return visited

  let frontier = [seedId]
  let hop = 0
  while (frontier.length > 0 && hop < maxHops) {
    hop++
    const next: string[] = []
    for (const currentId of frontier) {
      const current = index.byId.get(currentId)
      for (const neighborId of edgeMap.get(currentId) ?? []) {
        if (visited.has(neighborId) || neighborId === seedId) continue
        const neighbor = index.byId.get(neighborId)
        visited.set(neighborId, hop)
        const crossesServer = !current || !neighbor || current.server !== neighbor.server
        if (!crossesServer) next.push(neighborId)
      }
    }
    frontier = next
  }
  return visited
}
