import { isLinkNode, type CatalogIndex } from './catalog'

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

interface TraversalStep {
  id: string
  /** Object through which this connection was reached; absent for ordinary nodes and the focus. */
  via?: string
}

/** Independent traversals, preserving caller/target attribution through shared connections. */
export function getDirectionalNeighborhood(index: CatalogIndex, rootId: string, dependencies: number, dependents: number): string[] {
  const ids = new Set([rootId])
  const isLink = (id: string) => {
    const node = index.byId.get(id)
    return node !== undefined && isLinkNode(node)
  }
  // A connection can be reached through several objects, each with different
  // remote references. Deduplicating only by node id would lose those paths.
  const key = ({ id, via }: TraversalStep) => JSON.stringify([id, via])
  for (const [direction, limit] of [['outgoing', dependencies], ['incoming', dependents]] as const) {
    const adjacency = index[direction]
    function neighbors({ id, via }: TraversalStep): string[] {
      const adjacent = adjacency.get(id) ?? []
      if (via === undefined) return adjacent
      const attributed = new Set<string>()
      for (const ref of index.linkedServerRefsByLink.get(id) ?? []) {
        if (direction === 'outgoing' && ref.from === via && ref.to !== null) attributed.add(ref.to)
        if (direction === 'incoming' && ref.to === via) attributed.add(ref.from)
      }
      // Without attribution, keep the connection visible but do not infer
      // that every caller references every target (including older catalogs).
      return adjacent.filter(neighbor => attributed.has(neighbor))
    }
    const root: TraversalStep = { id: rootId }
    const seen = new Set([key(root)])
    let frontier = [root]
    for (let hop = 0; hop < limit && frontier.length; hop++) {
      const next: TraversalStep[] = []
      for (const step of frontier) for (const neighbor of neighbors(step)) {
        if (!index.byId.has(neighbor)) continue
        const nextStep = { id: neighbor, via: isLink(neighbor) ? step.id : undefined }
        const nextKey = key(nextStep)
        if (seen.has(nextKey)) continue
        seen.add(nextKey)
        ids.add(neighbor)
        next.push(nextStep)
      }
      frontier = next
    }
    // Show the remote target or caller beyond a connection, without turning onto
    // its unrelated branches or expanding a direction explicitly set to zero.
    if (limit > 0) for (const step of frontier) {
      if (!isLink(step.id)) continue
      for (const neighbor of neighbors(step)) {
        if (index.byId.has(neighbor)) ids.add(neighbor)
      }
    }
  }
  return [...ids]
}

/** Keep one shortest connecting path for each match, including filtered intermediates. */
export function retainConnectingPaths(index: CatalogIndex, rootId: string, ids: string[], matches: Set<string>): string[] {
  const allowed = new Set(ids)
  const parent = new Map<string, string | null>([[rootId, null]])
  const queue = [rootId]
  for (let i = 0; i < queue.length; i++) {
    const id = queue[i]
    for (const next of [...(index.outgoing.get(id) ?? []), ...(index.incoming.get(id) ?? [])]) {
      if (!allowed.has(next) || parent.has(next)) continue
      parent.set(next, id)
      queue.push(next)
    }
  }
  const kept = new Set([rootId])
  for (const match of ids) {
    if (!matches.has(match) || !parent.has(match)) continue
    let id: string | null = match
    while (id !== null && !kept.has(id)) {
      kept.add(id)
      id = parent.get(id) ?? null
    }
  }
  return ids.filter(id => kept.has(id))
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
  /** Present for optional intermediate-layer grouping. */
  hop?: number
}

/** Group only intermediate layers; leave the focus and the outermost objects visible. */
export function groupIntermediateLayers(index: CatalogIndex, focusId: string, ids: readonly string[]): BundledNeighborhood {
  const allowed = new Set(ids)
  const layers = new Map<string, { direction: 'outgoing' | 'incoming'; hop: number }>()
  for (const direction of ['outgoing', 'incoming'] as const) {
    const seen = new Set([focusId])
    let frontier = [focusId]
    for (let hop = 1; frontier.length; hop++) {
      const next: string[] = []
      for (const id of frontier) for (const neighbor of index[direction].get(id) ?? []) {
        if (!allowed.has(neighbor) || seen.has(neighbor)) continue
        seen.add(neighbor)
        next.push(neighbor)
        const previous = layers.get(neighbor)
        if (!previous || previous.hop > hop) layers.set(neighbor, { direction, hop })
      }
      frontier = next
    }
  }
  const groups = new Map<string, NeighborBundle>()
  for (const [id, layer] of layers) {
    // An intermediate has a continuation further away on the same side.
    const hasContinuation = (index[layer.direction].get(id) ?? []).some(next => {
      const target = layers.get(next)
      return target?.direction === layer.direction && target.hop > layer.hop
    })
    if (!hasContinuation) continue
    const type = index.byId.get(id)?.type
    if (!type) continue
    const key = `${BUNDLE_ID_PREFIX}:${layer.direction}:${layer.hop}:${type}`
    const group = groups.get(key)
    if (group) group.memberIds.push(id)
    else groups.set(key, { id: key, ...layer, type, memberIds: [id] })
  }
  const bundles = [...groups.values()].filter(group => group.memberIds.length > 1)
  const bundled = new Set(bundles.flatMap(group => group.memberIds))
  return { nodeIds: ids.filter(id => !bundled.has(id)), bundles, bundledCount: bundled.size }
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
