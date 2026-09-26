import { expect, it } from 'vitest'
import {
  applyFilters,
  decodeTokensFromUrl,
  describeToken,
  getFieldValue,
  getSuggestedValues,
  matchesToken,
  operatorsFor,
  type FilterToken,
} from './filters'
import { makeNode } from '../test/fixtures'
const token = (overrides: Partial<FilterToken> = {}): FilterToken => ({
  id: 'x',
  attribute: null,
  operator: 'contains',
  values: [],
  ...overrides,
})
it('searches each identity field and handles empty optional metadata', () => {
  const node = makeNode({
    id: 'x',
    qualifiedName: 'unique-name',
    server: 'unique-server',
    database: 'unique-db',
    schema: 'unique-schema',
    type: 'unique-type',
    description: 'unique-description',
    ddl: 'unique-sql',
    grants: [{ grantee: 'reader', granteeType: null, permission: 'SELECT', state: 'GRANT', column: null }],
  })
  for (const value of ['name', 'server', 'db', 'schema', 'type', 'description', 'sql'])
    expect(matchesToken(node, token({ values: [`unique-${value}`] }))).toBe(true)
  expect(matchesToken(makeNode({ id: 'empty', schema: null }), token({ values: ['absent'] }))).toBe(false)
  expect(matchesToken(node, token())).toBe(true)
  expect(getFieldValue(node, 'grantee')).toBe('reader')
  expect(matchesToken(node, token({ attribute: 'grantee', operator: 'is', values: ['reader'] }))).toBe(true)
  expect(getFieldValue(node, 'description')).toBe('unique-description')
  expect(getFieldValue(makeNode({ id: 'empty' }), 'description')).toBe('')
  expect(getFieldValue(makeNode({ id: 'empty', schema: null }), 'schema')).toBe('')
  expect(operatorsFor(null)).toEqual(['is', 'is-not', 'contains'])
})
it('applies every operator and handles empty token values', () => {
  const node = makeNode({ id: 'a', server: 'Host' })
  for (const [operator, values, expected] of [
    ['is', ['host'], true],
    ['is-not', ['host'], false],
    ['contains', ['os'], true],
    ['is-in', ['host', 'other'], true],
    ['is-not-in', ['host'], false],
  ] as const) {
    expect(matchesToken(node, token({ attribute: 'server', operator, values: [...values] }))).toBe(expected)
  }
  expect(matchesToken(node, token({ attribute: 'name', values: [''] }))).toBe(true)
  expect(applyFilters([node], [])).toEqual([node])
})
it('bounds suggestions and validates malformed URL payloads', () => {
  const nodes = Array.from({ length: 30 }, (_, i) => makeNode({ id: String(i), server: `S${i}` }))
  expect(getSuggestedValues(nodes, 'server', '', 1)).toEqual(['S0'])
  expect(getSuggestedValues([makeNode({ id: 'a', schema: null })], 'schema', '')).toEqual([])
  for (const raw of [
    'bad',
    '{}',
    JSON.stringify([
      null,
      [],
      ['name', 'is', 'bad'],
      ['name', 1, ['a']],
      ['name', 'is-in', ['a']],
      ['name', 'is', [null, '', ' ', 'a'.repeat(257)]],
    ]),
  ])
    expect(decodeTokensFromUrl(raw)).toEqual([])
  expect(describeToken(token())).toBe('')
  expect(describeToken(token({ attribute: 'unknown' as never, operator: 'is', values: ['a'] }))).toBe('unknown is a')
})
