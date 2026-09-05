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

/**
 * True when nothing but dynamically-built SQL produced this edge - the parser found
 * it inside an OPENQUERY body or an EXEC'd string, not on the parse tree. Worth
 * showing, because it is a weaker claim than an ordinary reference.
 */
export function isDynamicEdge(index: CatalogIndex, fromId: string, toId: string): boolean {
  return index.dynamicEdges.has(`${fromId}|${toId}`)
}

/** Marks a synthetic node id as a bundle rather than a real catalog object. */
export const BUNDLE_ID_PREFIX = '__bundle__'

export function isBundleId(id: string): boolean {
  return id.startsWith(`${BUNDLE_ID_PREFIX}:`)
}

/** One collapsed group of same-type neighbors, drawn as a single node with a count instead of N nodes. */
export interface NeighborBundle {
  id: string
  /** Which side of the focus object the members sit on - decides the synthetic edge's direction. */
  direction: 'outgoing' | 'incoming'
  /** The object type every member shares. */
  type: string
  memberIds: string[]
}

export interface BundledNeighborhood {
  /** Real catalog node ids to render (the focus object included). */
  nodeIds: string[]
  bundles: NeighborBundle[]
  /** How many real objects the bundles stand in for - 0 when nothing was collapsed. */
  bundledCount: number
}

/** True once the result actually collapsed something, i.e. the graph is showing a summary rather than everything. */
export function isBundled(result: BundledNeighborhood): boolean {
  return result.bundles.length > 0
}

export interface BundleOptions {
  /** Above this many nodes the graph stops being readable, so groups start collapsing. */
  maxNodes: number
  /** A single type group bigger than this collapses even when the whole neighborhood would fit. */
  maxPerGroup: number
}

/**
 * Below this a bundle is a net loss: "+2 Tables" hides two names to save one
 * node, and the reader has to click to learn less than the graph already told
 * them.
 */
const MIN_BUNDLE_SIZE = 3

/**
 * Collapses a hub object's direct neighbors into per-type bundles, so a node
 * with three hundred dependents renders as a readable star - "dbo.Orders &larr;
 * +212 Tables" - instead of a hairball dagre needs a second to lay out and
 * nobody can read afterwards.
 *
 * Small groups are kept whole and collapsed last (ascending size), so the two
 * procedures and one trigger around a hub stay individually visible while the
 * two hundred tables become one node. Anything that isn't a direct neighbor of
 * the focus (the outer rings of a multi-hop view) is left alone: attaching it to
 * a bundle hanging off the focus would draw a relationship that doesn't exist.
 *
 * Bundles are inspected by clicking rather than expanded in place - expanding a
 * 200-member bundle just rebuilds the hairball, and the object page's own
 * "Depends on / Used by" lists are where every name is meant to be read.
 */
export function bundleNeighborhood(
  index: CatalogIndex,
  focusId: string,
  ids: readonly string[],
  options: BundleOptions,
): BundledNeighborhood {
  // A neighborhood that already fits is never collapsed: bundling a graph the
  // reader could have read in full trades information for nothing.
  if (!index.byId.has(focusId) || ids.length <= options.maxNodes) {
    return { nodeIds: [...ids], bundles: [], bundledCount: 0 }
  }

  const outgoing = new Set(index.outgoing.get(focusId) ?? [])
  const incoming = new Set(index.incoming.get(focusId) ?? [])

  const groups = new Map<string, NeighborBundle>()
  const kept: string[] = []
  for (const id of ids) {
    if (id === focusId || !index.byId.has(id)) continue
    // A neighbor sitting on both sides is grouped as a dependency: the bundle's
    // only job is to place it relative to the focus, and the edges the graph
    // draws between real nodes still carry the full picture.
    const direction = outgoing.has(id) ? 'outgoing' : incoming.has(id) ? 'incoming' : null
    if (direction === null) {
      kept.push(id)
      continue
    }
    const type = index.byId.get(id)!.type
    const key = `${BUNDLE_ID_PREFIX}:${direction}:${type}`
    const group = groups.get(key)
    if (group) group.memberIds.push(id)
    else groups.set(key, { id: key, direction, type, memberIds: [id] })
  }

  // Ascending size: whatever budget is left goes to the groups that cost the
  // least to show in full, so the long tail of one-off neighbors survives.
  const ordered = [...groups.values()].sort((a, b) => a.memberIds.length - b.memberIds.length || a.id.localeCompare(b.id))

  const bundles: NeighborBundle[] = []
  let budget = Math.max(options.maxNodes - 1 - kept.length, 0)

  for (const group of ordered) {
    const size = group.memberIds.length
    const tooBig = size > options.maxPerGroup || size > budget
    if (size < MIN_BUNDLE_SIZE || !tooBig) {
      kept.push(...group.memberIds)
      budget -= size
      continue
    }
    bundles.push(group)
    budget -= 1
  }

  return {
    nodeIds: [focusId, ...kept],
    bundles,
    bundledCount: bundles.reduce((sum, bundle) => sum + bundle.memberIds.length, 0),
  }
}
