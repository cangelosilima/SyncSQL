import type { CatalogNode } from '../types'

export type FilterAttribute = 'server' | 'database' | 'schema' | 'type' | 'name' | 'description'
export type FilterOperator = 'is' | 'is-not' | 'contains' | 'is-in' | 'is-not-in'

export interface FilterToken {
  id: string
  /** null = plain-text token: substring search across the common text fields. */
  attribute: FilterAttribute | null
  operator: FilterOperator
  values: string[]
}

export interface FilterAttributeDef {
  key: FilterAttribute
  label: string
  kind: 'enum' | 'text'
}

export const FILTER_ATTRIBUTES: FilterAttributeDef[] = [
  { key: 'server', label: 'Server', kind: 'enum' },
  { key: 'database', label: 'Database', kind: 'enum' },
  { key: 'schema', label: 'Schema', kind: 'enum' },
  { key: 'type', label: 'Type', kind: 'enum' },
  { key: 'name', label: 'Name', kind: 'text' },
  { key: 'description', label: 'Description', kind: 'text' },
]

export const OPERATOR_LABELS: Record<FilterOperator, string> = {
  is: 'is',
  'is-not': 'is not',
  contains: 'contains',
  'is-in': 'is in',
  'is-not-in': 'is not in',
}

export function operatorsFor(attribute: FilterAttribute | null): FilterOperator[] {
  const def = FILTER_ATTRIBUTES.find((a) => a.key === attribute)
  const kind = def?.kind ?? 'text'
  return kind === 'enum' ? ['is', 'is-not', 'is-in', 'is-not-in'] : ['is', 'is-not', 'contains']
}

export function getFieldValue(node: CatalogNode, attribute: FilterAttribute): string {
  switch (attribute) {
    case 'server':
      return node.server
    case 'database':
      return node.database
    case 'schema':
      return node.schema ?? ''
    case 'type':
      return node.type
    case 'name':
      return node.qualifiedName
    case 'description':
      return node.description ?? ''
  }
}

/**
 * Unique values for an attribute, filtered by a typed query, capped for
 * performance on large catalogs. Stops scanning early once it has collected
 * comfortably more candidates than the cap so a huge node list doesn't get
 * fully walked just to populate a suggestion dropdown.
 */
export function getSuggestedValues(nodes: CatalogNode[], attribute: FilterAttribute, query: string, limit = 50): string[] {
  const needle = query.trim().toLowerCase()
  const set = new Set<string>()
  const scanCap = limit * 20
  for (const node of nodes) {
    const value = getFieldValue(node, attribute)
    if (value && (!needle || value.toLowerCase().includes(needle))) set.add(value)
    if (set.size >= scanCap) break
  }
  return [...set].sort((a, b) => a.localeCompare(b)).slice(0, limit)
}

export function matchesToken(node: CatalogNode, token: FilterToken): boolean {
  if (!token.attribute) {
    const needle = (token.values[0] ?? '').toLowerCase()
    if (!needle) return true
    return (
      node.qualifiedName.toLowerCase().includes(needle) ||
      node.server.toLowerCase().includes(needle) ||
      node.database.toLowerCase().includes(needle) ||
      (node.schema ?? '').toLowerCase().includes(needle) ||
      (node.description ?? '').toLowerCase().includes(needle)
    )
  }

  const fieldValue = getFieldValue(node, token.attribute).toLowerCase()
  const values = token.values.map((v) => v.toLowerCase()).filter(Boolean)
  if (values.length === 0) return true

  switch (token.operator) {
    case 'is':
      return fieldValue === values[0]
    case 'is-not':
      return fieldValue !== values[0]
    case 'contains':
      return fieldValue.includes(values[0])
    case 'is-in':
      return values.includes(fieldValue)
    case 'is-not-in':
      return !values.includes(fieldValue)
  }
}

export function applyFilters(nodes: CatalogNode[], tokens: FilterToken[]): CatalogNode[] {
  if (tokens.length === 0) return nodes
  return nodes.filter((node) => tokens.every((token) => matchesToken(node, token)))
}

export function describeToken(token: FilterToken): string {
  if (!token.attribute) return token.values[0] ?? ''
  const attrLabel = FILTER_ATTRIBUTES.find((a) => a.key === token.attribute)?.label ?? token.attribute
  return `${attrLabel} ${OPERATOR_LABELS[token.operator]} ${token.values.join(', ')}`
}

let tokenSeq = 0
export function newTokenId(): string {
  tokenSeq += 1
  return `tok-${Date.now()}-${tokenSeq}`
}
