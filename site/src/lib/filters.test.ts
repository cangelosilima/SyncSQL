import { describe, expect, it } from 'vitest'
import { decodeTokensFromUrl, encodeTokensForUrl } from './filters'

describe('filter URL encoding', () => {
  it('round-trips validated filter tokens', () => {
    const encoded = encodeTokensForUrl([
      { attribute: 'database', operator: 'is', values: ['AppDb'] },
      { attribute: 'type', operator: 'is-in', values: ['Tables', 'Views'] },
    ])
    expect(decodeTokensFromUrl(encoded).map(({ id: _id, ...token }) => token)).toEqual([
      { attribute: 'database', operator: 'is', values: ['AppDb'] },
      { attribute: 'type', operator: 'is-in', values: ['Tables', 'Views'] },
    ])
  })

  it('drops unknown attributes, operators, and empty values', () => {
    const encoded = JSON.stringify([
      ['engine', 'is', ['oracle']],
      ['database', 'exec', ['AppDb']],
      ['schema', 'is', []],
    ])
    expect(decodeTokensFromUrl(encoded)).toEqual([])
  })
})
