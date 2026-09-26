// @vitest-environment node
import { readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { afterEach, expect, it, vi } from 'vitest'
const catalog = JSON.parse(readFileSync(new URL('../public/data/catalog.json', import.meta.url), 'utf8'))
const contract = JSON.parse(
  readFileSync(new URL('../../samples/scenarios/heterogeneous-lineage/expected-catalog.json', import.meta.url), 'utf8'),
)
const original = process.argv
afterEach(() => {
  process.argv = original
  vi.restoreAllMocks()
  vi.doUnmock('node:fs/promises')
  vi.resetModules()
})
it.each(['--check', 'run-directory', undefined])('runs example CLI with %s', async (argument) => {
  const writeFile = vi.fn().mockResolvedValue(undefined)
  vi.doMock('node:fs/promises', () => ({
    writeFile,
    readFile: vi.fn(async (file: string) =>
      JSON.stringify(
        file.endsWith('expected-catalog.json')
          ? contract
          : file.endsWith('benchmark.json')
            ? {
                scenario: 'heterogeneous-lineage',
                passed: true,
                gatewayRequested: true,
                oracleToSqlServerExecution: 'passed: verified',
              }
            : catalog,
      ),
    ),
  }))
  const log = vi.spyOn(console, 'log').mockImplementation(() => {})
  process.argv = [
    original[0],
    fileURLToPath(new URL('./catalog-example.mjs', import.meta.url)),
    ...(argument ? [argument] : []),
  ]
  vi.resetModules()
  if (!argument) await expect(import('./catalog-example.mjs')).rejects.toThrow('Usage:')
  else {
    await import('./catalog-example.mjs')
    expect(log).toHaveBeenCalledWith(expect.stringContaining(argument === '--check' ? 'Verified benchmark' : 'Updated'))
    if (argument !== '--check') expect(JSON.parse(writeFile.mock.calls[0][1]).example.gatewayVerified).toBe(true)
  }
})
it('imports without a CLI entry and validates masked password quoting styles', async () => {
  process.argv = [original[0]]
  vi.resetModules()
  const { validateExample, prepareExample } = await import('./catalog-example.mjs')
  const copy = structuredClone(catalog)
  delete copy.example
  delete copy.orphanedReferences
  copy.nodes[0].ddl += '\nIDENTIFIED BY "***"; IDENTIFIED BY ###;'
  expect(() => validateExample(copy, contract)).not.toThrow()
  expect(
    prepareExample(copy, contract, {
      scenario: 'heterogeneous-lineage',
      passed: true,
      gatewayRequested: true,
      oracleToSqlServerExecution: 'passed: verified',
    }).example.gatewayVerified,
  ).toBe(true)
})
