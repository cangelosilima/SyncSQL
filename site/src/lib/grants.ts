import type { CatalogGrant, CatalogNode } from '../types'

export interface GranteeMatch {
  node: CatalogNode
  grants: CatalogGrant[]
}

/**
 * Distinct grantee names across the whole catalog, capped for suggestion-list
 * performance the same way lib/filters.ts's getSuggestedValues is - see that
 * file for the rationale.
 */
export function getSuggestedGrantees(nodes: CatalogNode[], query: string, limit = 50): string[] {
  const needle = query.trim().toLowerCase()
  const set = new Set<string>()
  const scanCap = limit * 20
  for (const node of nodes) {
    for (const grant of node.grants) {
      if (!needle || grant.grantee.toLowerCase().includes(needle)) set.add(grant.grantee)
      if (set.size >= scanCap) break
    }
  }
  return [...set].sort((a, b) => a.localeCompare(b)).slice(0, limit)
}

/**
 * Every object a grantee has any GRANT/DENY permission on, matched by
 * case-insensitive substring (or exact match when `exact` is set) against
 * grantee name. Powers the Access page's "what can this principal touch"
 * search.
 */
export function findObjectsForGrantee(nodes: CatalogNode[], query: string, exact = false): GranteeMatch[] {
  const needle = query.trim().toLowerCase()
  if (!needle) return []

  const results: GranteeMatch[] = []
  for (const node of nodes) {
    const matching = node.grants.filter((g) =>
      exact ? g.grantee.toLowerCase() === needle : g.grantee.toLowerCase().includes(needle),
    )
    if (matching.length > 0) results.push({ node, grants: matching })
  }
  return results
}
