import {
  applyFilters,
  decodeTokensFromUrl,
  describeToken,
  encodeTokensForUrl,
  getFieldValue,
  getSuggestedValues,
  matchesToken,
  newTokenId,
  operatorsFor,
  type FilterToken,
} from './filters'
import { makeNode } from '../test/fixtures'

function token(partial: Omit<FilterToken, 'id'>): FilterToken {
  return { id: newTokenId(), ...partial }
}

const orders = makeNode({
  id: 'orders',
  server: 'SRV1',
  database: 'AppDb',
  schema: 'dbo',
  type: 'Tables',
  name: 'Orders',
  qualifiedName: 'SRV1.AppDb.dbo.Orders',
  description: 'Customer orders',
})
const report = makeNode({
  id: 'report',
  server: 'SRV2',
  database: 'Warehouse',
  schema: null,
  type: 'Views',
  name: 'SalesReport',
  qualifiedName: 'SRV2.Warehouse.SalesReport',
})

describe('operatorsFor', () => {
  it('offers set operators for enum attributes', () => {
    expect(operatorsFor('type')).toEqual(['is', 'is-not', 'is-in', 'is-not-in'])
  })

  it('offers contains for free-text attributes', () => {
    expect(operatorsFor('name')).toEqual(['is', 'is-not', 'contains'])
  })

  it('falls back to text operators for a plain-text token', () => {
    expect(operatorsFor(null)).toEqual(['is', 'is-not', 'contains'])
  })
})

describe('getFieldValue', () => {
  it('maps name to the qualified name, not the bare one', () => {
    expect(getFieldValue(orders, 'name')).toBe('SRV1.AppDb.dbo.Orders')
  })

  it('renders a null schema and a null description as empty strings', () => {
    expect(getFieldValue(report, 'schema')).toBe('')
    expect(getFieldValue(report, 'description')).toBe('')
  })
})

describe('matchesToken', () => {
  it('compares exactly and case-insensitively for "is"', () => {
    expect(matchesToken(orders, token({ attribute: 'type', operator: 'is', values: ['tables'] }))).toBe(true)
    expect(matchesToken(orders, token({ attribute: 'type', operator: 'is', values: ['Table'] }))).toBe(false)
  })

  it('inverts the comparison for "is-not"', () => {
    expect(matchesToken(orders, token({ attribute: 'type', operator: 'is-not', values: ['Views'] }))).toBe(true)
  })

  it('does a substring match for "contains"', () => {
    expect(matchesToken(orders, token({ attribute: 'name', operator: 'contains', values: ['dbo.Ord'] }))).toBe(true)
  })

  it('handles the set operators', () => {
    expect(matchesToken(orders, token({ attribute: 'server', operator: 'is-in', values: ['SRV1', 'SRV3'] }))).toBe(true)
    expect(matchesToken(orders, token({ attribute: 'server', operator: 'is-not-in', values: ['SRV1', 'SRV3'] }))).toBe(false)
  })

  it('searches across the common text fields for a plain-text token', () => {
    expect(matchesToken(orders, token({ attribute: null, operator: 'contains', values: ['warehouse'] }))).toBe(false)
    expect(matchesToken(orders, token({ attribute: null, operator: 'contains', values: ['customer orders'] }))).toBe(true)
    expect(matchesToken(orders, token({ attribute: null, operator: 'contains', values: ['appdb'] }))).toBe(true)
  })

  it('treats a token with no usable value as matching everything', () => {
    expect(matchesToken(orders, token({ attribute: null, operator: 'contains', values: [''] }))).toBe(true)
    expect(matchesToken(orders, token({ attribute: 'type', operator: 'is', values: [] }))).toBe(true)
  })
})

describe('applyFilters', () => {
  const nodes = [orders, report]

  it('returns the original array when there is nothing to filter by', () => {
    expect(applyFilters(nodes, [])).toBe(nodes)
  })

  it('ANDs the tokens together', () => {
    const result = applyFilters(nodes, [
      token({ attribute: 'server', operator: 'is', values: ['SRV1'] }),
      token({ attribute: 'type', operator: 'is', values: ['Tables'] }),
    ])
    expect(result.map((n) => n.id)).toEqual(['orders'])
  })

  it('returns nothing when the tokens contradict each other', () => {
    const result = applyFilters(nodes, [
      token({ attribute: 'server', operator: 'is', values: ['SRV1'] }),
      token({ attribute: 'server', operator: 'is', values: ['SRV2'] }),
    ])
    expect(result).toEqual([])
  })
})

describe('getSuggestedValues', () => {
  const nodes = [orders, report, makeNode({ id: 'dup', server: 'SRV1' })]

  it('de-duplicates and sorts', () => {
    expect(getSuggestedValues(nodes, 'server', '')).toEqual(['SRV1', 'SRV2'])
  })

  it('filters by the typed query, case-insensitively', () => {
    expect(getSuggestedValues(nodes, 'database', 'ware')).toEqual(['Warehouse'])
  })

  it('skips empty values such as a null schema', () => {
    expect(getSuggestedValues([report], 'schema', '')).toEqual([])
  })

  it('respects the limit', () => {
    expect(getSuggestedValues(nodes, 'server', '', 1)).toEqual(['SRV1'])
  })
})

describe('describeToken', () => {
  it('renders a human-readable label', () => {
    expect(describeToken(token({ attribute: 'type', operator: 'is-in', values: ['Tables', 'Views'] }))).toBe(
      'Type is in Tables, Views',
    )
  })

  it('renders a plain-text token as its raw value', () => {
    expect(describeToken(token({ attribute: null, operator: 'contains', values: ['orders'] }))).toBe('orders')
  })
})

describe('URL round-trip', () => {
  it('preserves attribute, operator and values while dropping the ephemeral id', () => {
    const tokens = [
      token({ attribute: 'server', operator: 'is', values: ['SRV1'] }),
      token({ attribute: null, operator: 'contains', values: ['orders'] }),
    ]
    const decoded = decodeTokensFromUrl(encodeTokensForUrl(tokens))
    expect(decoded.map(({ attribute, operator, values }) => ({ attribute, operator, values }))).toEqual([
      { attribute: 'server', operator: 'is', values: ['SRV1'] },
      { attribute: null, operator: 'contains', values: ['orders'] },
    ])
  })

  it('assigns each decoded token a fresh unique id', () => {
    const decoded = decodeTokensFromUrl(encodeTokensForUrl([token({ attribute: 'server', operator: 'is', values: ['SRV1'] })]))
    expect(decoded[0].id).toBeTruthy()
  })

  it('yields no tokens for a missing, malformed or wrongly-shaped param', () => {
    expect(decodeTokensFromUrl(null)).toEqual([])
    expect(decodeTokensFromUrl('not json')).toEqual([])
    expect(decodeTokensFromUrl('{"a":1}')).toEqual([])
  })

  it('drops entries that are not a complete [attribute, operator, values] triple', () => {
    expect(decodeTokensFromUrl('[["server","is"],["type","is",["Tables"]]]').map((t) => t.attribute)).toEqual(['type'])
  })
})

describe('newTokenId', () => {
  it('never repeats itself', () => {
    const ids = Array.from({ length: 100 }, () => newTokenId())
    expect(new Set(ids).size).toBe(100)
  })
})
