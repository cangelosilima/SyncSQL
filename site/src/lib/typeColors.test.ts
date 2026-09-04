import { describe, expect, it } from 'vitest'
import { colorForType } from './typeColors'

describe('colorForType', () => {
  it('returns a hex colour for a known type', () => {
    expect(colorForType('Tables')).toMatch(/^#[0-9a-f]{6}$/)
  })

  it('gives the MSSQL and Oracle names for the same concept the same colour', () => {
    expect(colorForType('StoredProcedures')).toBe(colorForType('Procedures'))
    expect(colorForType('LinkedServers')).toBe(colorForType('DatabaseLinks'))
  })

  it('falls back to a single neutral colour for unknown types', () => {
    expect(colorForType('Sequences')).toBe(colorForType('SomethingElse'))
    expect(colorForType('Sequences')).not.toBe(colorForType('Tables'))
  })
})
