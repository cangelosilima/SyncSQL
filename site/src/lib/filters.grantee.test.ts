import { expect, it } from 'vitest'
import {
  applyFilters,
  decodeTokensFromUrl,
  encodeTokensForUrl,
  getSuggestedValues,
  matchingGrants,
  type FilterToken,
} from './filters'
import { makeNode } from '../test/fixtures'

const grant = (grantee: string) => ({ grantee, permission: 'SELECT', state: 'GRANT', granteeType: null, column: null })
const nodes = [
  makeNode({ id: 'orders', grants: [grant('reader'), grant('reader_extra')] }),
  makeNode({ id: 'users', grants: [grant('reader_extra')] }),
]
const token = (operator: FilterToken['operator'], values = ['reader']): FilterToken => ({
  id: 'user',
  attribute: 'grantee',
  operator,
  values,
})

it('supports exact, partial and multiple grantees with distinct suggestions and URL round trips', () => {
  expect(getSuggestedValues(nodes, 'grantee', 'read')).toEqual(['reader', 'reader_extra'])
  expect(applyFilters(nodes, [token('is')]).map((n) => n.id)).toEqual(['orders'])
  expect(applyFilters(nodes, [token('contains')])).toHaveLength(2)
  expect(applyFilters(nodes, [token('is-in', ['reader', 'reader_extra'])])).toHaveLength(2)
  expect(decodeTokensFromUrl(encodeTokensForUrl([token('contains')]))[0]).toMatchObject({
    attribute: 'grantee',
    operator: 'contains',
    values: ['reader'],
  })
})

it('applies grantee tokens to the same permission record and exports only those records', () => {
  expect(matchingGrants(nodes[0], [token('is')])).toEqual([grant('reader')])
  expect(applyFilters(nodes, [token('is'), token('is-not')])).toEqual([])
  expect(
    applyFilters(nodes, [token('is'), { id: 'sql', attribute: 'ddl', operator: 'contains', values: ['missing SQL'] }]),
  ).toEqual([])
})
