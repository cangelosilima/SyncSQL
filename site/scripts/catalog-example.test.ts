// @vitest-environment node
import { readFileSync } from 'node:fs'
import { describe, expect, it } from 'vitest'
import { validateExample, prepareExample } from './catalog-example.mjs'

const read = (path: string) => JSON.parse(readFileSync(new URL(path, import.meta.url), 'utf8'))
const contract = read('../../samples/scenarios/heterogeneous-lineage/expected-catalog.json')
const catalog = read('../public/data/catalog.json')

describe('published benchmark example', () => {
  it('matches every identity, column, grant, edge and attributed remote reference in the independent contract', () => {
    expect(() => validateExample(catalog, contract)).not.toThrow()
    expect(catalog.example.users).toHaveLength(48)
    expect(catalog.example.paths).toEqual(contract.paths)
    expect(catalog.example.gatewayVerified).toBe(true)
  })

  it('rejects missing edges, lost permissions and misattributed remote paths', () => {
    for (const mutate of [
      (copy: typeof catalog) => copy.edges.pop(),
      (copy: typeof catalog) => { copy.nodes.find((n: { grants: unknown[] }) => n.grants.length).grants = [] },
      (copy: typeof catalog) => { copy.linkedServerReferences[0].to = copy.linkedServerReferences[1].to + '-wrong' },
    ]) {
      const copy = structuredClone(catalog)
      mutate(copy)
      expect(() => validateExample(copy, contract)).toThrow()
    }
  })

  it('requires a successful live gateway run and rejects unmasked passwords before publication', () => {
    expect(() => prepareExample(catalog, contract, { scenario: 'heterogeneous-lineage', passed: false })).toThrow(/must pass/)
    expect(() => prepareExample(catalog, contract, { scenario: 'heterogeneous-lineage', passed: true, gatewayRequested: false })).toThrow(/gateway/i)
    const copy = structuredClone(catalog)
    copy.nodes[0].ddl += "\nCREATE USER accidental IDENTIFIED BY 'do-not-publish';"
    expect(() => validateExample(copy, contract)).toThrow(/password/i)
  })
})
