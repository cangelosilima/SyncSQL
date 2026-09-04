import type { CatalogNode } from '../types'
import type { FilterAttribute, FilterOperator, FilterTokenInput } from '../lib/filters'
import type { EmbeddingAdapter, FilterPlanV1, FilterPlanner } from './types'

const MIN_SIMILARITY = 0.45
const MIN_MARGIN = 0.05

const INTENT_PROTOTYPES: Array<{ attribute: FilterAttribute | 'content'; text: string }> = [
  { attribute: 'server', text: 'filter database objects by server host or machine' },
  { attribute: 'database', text: 'filter database objects inside a particular database' },
  { attribute: 'schema', text: 'filter database objects belonging to a schema' },
  { attribute: 'type', text: 'filter objects by type such as tables views procedures functions or triggers' },
  { attribute: 'name', text: 'find an object whose name is called or contains text' },
  { attribute: 'description', text: 'find an object whose description or documentation contains text' },
  { attribute: 'content', text: 'find SQL definition DDL code that mentions references uses or contains text' },
]

const TYPE_ALIASES: Array<{ pattern: RegExp; target: string }> = [
  { pattern: /\blinked\s+servers?\b/i, target: 'linkedserver' },
  { pattern: /\bdatabase\s+links?\b/i, target: 'databaselink' },
  { pattern: /\bpackage\s+bodies\b/i, target: 'packagebod' },
  { pattern: /\bstored\s+procedures?\b/i, target: 'storedprocedure' },
  { pattern: /\bprocs?\b/i, target: 'procedure' },
  { pattern: /\bprocedures?\b/i, target: 'procedure' },
  { pattern: /\bfunctions?\b/i, target: 'function' },
  { pattern: /\btriggers?\b/i, target: 'trigger' },
  { pattern: /\bsynonyms?\b/i, target: 'synonym' },
  { pattern: /\breplication\b/i, target: 'replication' },
  { pattern: /\bpackages?\b/i, target: 'package' },
  { pattern: /\bviews?\b/i, target: 'view' },
  { pattern: /\btables?\b/i, target: 'table' },
]

interface Facets {
  server: string[]
  database: string[]
  schema: string[]
  type: string[]
}

export class EmbeddingFilterPlanner implements FilterPlanner {
  async generate(query: string, nodes: CatalogNode[], adapter: EmbeddingAdapter, signal?: AbortSignal): Promise<FilterPlanV1> {
    const trimmed = query.trim()
    if (!trimmed) return emptyPlan('Enter a filter request.')

    const facets = buildFacets(nodes)
    const tokens: FilterTokenInput[] = []
    const contentQueries: string[] = []
    const warnings: string[] = []
    const unsupportedFragments: string[] = []
    let usedSemanticClassification = false

    const clauses = splitClauses(trimmed)
    for (const clause of clauses) {
      if (signal?.aborted) throw abortError()
      let handled = false

      for (const attribute of ['server', 'database', 'schema'] as const) {
        const matches = findFacetMatches(clause, facets[attribute])
        if (matches.length === 0) continue
        addToken(tokens, attribute, operatorForMatches(clause, matches), matches.map((match) => match.value))
        handled = true
      }

      const explicitTypeMatches = findFacetMatches(clause, facets.type)
      const aliasTypeMatches = findTypeAliasMatches(clause, facets.type)
      const typeMatches = aliasTypeMatches.length > 0 ? aliasTypeMatches : explicitTypeMatches
      if (typeMatches.length > 0) {
        addToken(tokens, 'type', operatorForMatches(clause, typeMatches), typeMatches.map((match) => match.value))
        handled = true
      }

      const contentValue = extractCueValue(clause, /\b(?:mentions?|references?|uses?|ddl\s+(?:contains?|mentions?)|definition\s+(?:contains?|mentions?)|code\s+(?:contains?|mentions?))\b/i)
      if (contentValue) {
        contentQueries.push(cleanLiteral(contentValue, facets))
        handled = true
      }

      const nameValue = extractCueValue(clause, /\b(?:named|called|name\s+(?:is|contains?))\b/i)
      if (nameValue) {
        addToken(tokens, 'name', isNegatedNear(clause, nameValue) ? 'is-not' : 'contains', [cleanLiteral(nameValue, facets)])
        handled = true
      }

      const descriptionValue = extractCueValue(clause, /\b(?:description|described\s+as|documented\s+as)\b/i)
      if (descriptionValue) {
        addToken(tokens, 'description', isNegatedNear(clause, descriptionValue) ? 'is-not' : 'contains', [cleanLiteral(descriptionValue, facets)])
        handled = true
      }

      if (!handled) {
        const intent = await classifyIntent(clause, adapter, signal)
        usedSemanticClassification = true
        if (!intent || intent.score < MIN_SIMILARITY || intent.margin < MIN_MARGIN) {
          unsupportedFragments.push(clause)
          warnings.push(`Could not confidently interpret “${clause}”.`)
          continue
        }

        const literal = extractQuotedValue(clause) ?? fallbackLiteral(clause, intent.attribute)
        if (intent.attribute === 'content' && literal) {
          contentQueries.push(literal)
        } else if ((intent.attribute === 'name' || intent.attribute === 'description') && literal) {
          addToken(tokens, intent.attribute, isNegatedNear(clause, literal) ? 'is-not' : 'contains', [literal])
        } else {
          unsupportedFragments.push(clause)
          warnings.push(`Recognized ${intent.attribute} intent but could not resolve a catalog value in “${clause}”.`)
        }
      }
    }

    const distinctContent = [...new Set(contentQueries.map((value) => value.trim()).filter(Boolean))]
    let contentQuery = distinctContent[0] ?? ''
    if (distinctContent.length > 1) {
      contentQuery = ''
      warnings.push('Only one DDL content phrase is supported at a time.')
      unsupportedFragments.push(...distinctContent)
    }

    const cleanTokens = tokens.filter((token) => token.values.every(Boolean))
    const hasOutput = cleanTokens.length > 0 || Boolean(contentQuery)
    const confidence = !hasOutput || warnings.length > 0 || unsupportedFragments.length > 0
      ? 'low'
      : usedSemanticClassification
        ? 'medium'
        : 'high'

    return { version: 1, tokens: cleanTokens, contentQuery, confidence, warnings, unsupportedFragments }
  }
}

function buildFacets(nodes: CatalogNode[]): Facets {
  return {
    server: unique(nodes.map((node) => node.server)),
    database: unique(nodes.map((node) => node.database)),
    schema: unique(nodes.map((node) => node.schema ?? '')),
    type: unique(nodes.map((node) => node.type)),
  }
}

function unique(values: string[]): string[] {
  return [...new Set(values.filter(Boolean))].sort((a, b) => b.length - a.length || a.localeCompare(b))
}

function splitClauses(query: string): string[] {
  return query.split(/\s+(?:and|but)\s+|[,;]+/i).map((part) => part.trim()).filter(Boolean)
}

interface ValueMatch {
  value: string
  index: number
}

function findFacetMatches(clause: string, values: string[]): ValueMatch[] {
  const normalizedClause = normalize(clause)
  const matches: ValueMatch[] = []
  for (const value of values) {
    const needle = normalize(value)
    const index = ` ${normalizedClause} `.indexOf(` ${needle} `)
    if (index >= 0) matches.push({ value, index: Math.max(0, index - 1) })
  }
  return matches
}

function findTypeAliasMatches(clause: string, types: string[]): ValueMatch[] {
  for (const alias of TYPE_ALIASES) {
    const match = alias.pattern.exec(clause)
    if (!match) continue
    const values = types.filter((type) => normalize(type).split(' ').join('').includes(alias.target))
    if (values.length > 0) return values.map((value) => ({ value, index: match.index }))
  }
  return []
}

function operatorForMatches(clause: string, matches: ValueMatch[]): FilterOperator {
  const negative = matches.some((match) => isNegatedAt(clause, match.index))
  if (matches.length > 1) return negative ? 'is-not-in' : 'is-in'
  return negative ? 'is-not' : 'is'
}

function isNegatedAt(clause: string, index: number): boolean {
  const prefix = clause.slice(Math.max(0, index - 32), index)
  return /\b(?:not|except|excluding|without)\b[^,;]*$/i.test(prefix)
}

function isNegatedNear(clause: string, literal: string): boolean {
  const index = clause.toLowerCase().indexOf(literal.toLowerCase())
  return isNegatedAt(clause, index < 0 ? clause.length : index)
}

function extractCueValue(clause: string, cue: RegExp): string | null {
  const match = cue.exec(clause)
  if (!match) return null
  const rest = clause.slice(match.index + match[0].length).trim().replace(/^(?:is|for|with|that|which|to)\s+/i, '')
  return extractQuotedValue(rest) ?? (rest.replace(/[.?!]+$/, '').trim() || null)
}

function extractQuotedValue(text: string): string | null {
  const match = /["'`](.+?)["'`]/.exec(text)
  return match?.[1]?.trim() || null
}

function cleanLiteral(value: string, facets: Facets): string {
  let result = value.trim()
  for (const facetValue of [...facets.server, ...facets.database, ...facets.schema, ...facets.type]) {
    result = result.replace(new RegExp(`\\b(?:in|on|from|under)\\s+${escapeRegex(facetValue)}\\b`, 'i'), '')
  }
  return result.replace(/\s+/g, ' ').replace(/[.?!]+$/, '').trim()
}

function fallbackLiteral(clause: string, attribute: FilterAttribute | 'content'): string | null {
  const withoutFraming = clause
    .replace(/\b(?:show|find|list|objects?|where|whose|that|which|filter|by|please|me)\b/gi, ' ')
    .replace(new RegExp(`\\b${attribute === 'content' ? '(?:ddl|definition|code|content)' : attribute}\\b`, 'gi'), ' ')
    .replace(/\b(?:contains?|mentions?|references?|uses?|named|called|is|are)\b/gi, ' ')
    .replace(/\s+/g, ' ')
    .trim()
  return withoutFraming || null
}

async function classifyIntent(clause: string, adapter: EmbeddingAdapter, signal?: AbortSignal) {
  const vectors = await adapter.embed([clause, ...INTENT_PROTOTYPES.map((prototype) => prototype.text)], signal)
  const queryVector = vectors[0]
  if (!queryVector) return null
  const ranked = INTENT_PROTOTYPES.map((prototype, index) => ({
    attribute: prototype.attribute,
    score: dot(queryVector, vectors[index + 1] ?? []),
  })).sort((a, b) => b.score - a.score)
  const first = ranked[0]
  if (!first) return null
  return { ...first, margin: first.score - (ranked[1]?.score ?? 0) }
}

function dot(a: number[], b: number[]): number {
  let total = 0
  const length = Math.min(a.length, b.length)
  for (let index = 0; index < length; index += 1) total += a[index] * b[index]
  return total
}

function addToken(tokens: FilterTokenInput[], attribute: FilterAttribute, operator: FilterOperator, values: string[]) {
  const cleanValues = [...new Set(values.map((value) => value.trim()).filter(Boolean))]
  if (cleanValues.length === 0) return
  const key = `${attribute}|${operator}|${cleanValues.join('|').toLowerCase()}`
  const exists = tokens.some((token) => `${token.attribute}|${token.operator}|${token.values.join('|').toLowerCase()}` === key)
  if (!exists) tokens.push({ attribute, operator, values: cleanValues })
}

function normalize(value: string): string {
  return value
    .normalize('NFKD')
    .replace(/[\u0300-\u036f]/g, '')
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, ' ')
    .trim()
}

function escapeRegex(value: string): string {
  return value.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')
}

function abortError(): DOMException {
  return new DOMException('The operation was aborted.', 'AbortError')
}

function emptyPlan(message: string): FilterPlanV1 {
  return { version: 1, tokens: [], contentQuery: '', confidence: 'low', warnings: [message], unsupportedFragments: [] }
}
