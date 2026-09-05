import { describe, expect, it } from 'vitest'
import { makeNode } from '../test/fixtures'
import { findObjectsForGrantee, getSuggestedGrantees } from './grants'

const grant = (grantee: string) => ({ grantee, permission: 'SELECT', state: 'GRANT' as const, granteeType: null, column: null })
const nodes = [makeNode({ id: 'orders', grants: [grant('reader'), grant('admin'), grant('reader')] }), makeNode({ id: 'customers', grants: [grant('reader_team')] }), makeNode({ id: 'private' })]

describe('grantee lookup', () => {
  it('deduplicates, sorts, filters and limits suggestions', () => {
    expect(getSuggestedGrantees(nodes, '')).toEqual(['admin', 'reader', 'reader_team'])
    expect(getSuggestedGrantees(nodes, ' READ ')).toEqual(['reader', 'reader_team'])
    expect(getSuggestedGrantees(nodes, '', 1)).toEqual(['admin'])
    expect(getSuggestedGrantees(nodes, 'missing')).toEqual([])
  })

  it('stops scanning once the suggestion candidate cap is reached', () => {
    const many = makeNode({ id: 'many', grants: Array.from({ length: 21 }, (_, i) => grant(i < 20 ? `z${i}` : 'aaa')) })
    expect(getSuggestedGrantees([many], '', 1)).toEqual(['z0'])
  })

  it('returns only matching permissions with substring or exact matching', () => {
    expect(findObjectsForGrantee(nodes, ' READ ').map(({ node }) => node.id)).toEqual(['orders', 'customers'])
    expect(findObjectsForGrantee(nodes, ' READER ', true)).toEqual([{ node: nodes[0], grants: [grant('reader'), grant('reader')] }])
    expect(findObjectsForGrantee(nodes, ' ')).toEqual([])
    expect(findObjectsForGrantee(nodes, 'missing')).toEqual([])
  })
})
