import assert from 'node:assert/strict'
import { readFile, writeFile } from 'node:fs/promises'
import path from 'node:path'
import { fileURLToPath } from 'node:url'

const siteRoot = fileURLToPath(new URL('..', import.meta.url))
const contractFile = path.resolve(siteRoot, '../samples/scenarios/heterogeneous-lineage/expected-catalog.json')
const catalogFile = path.join(siteRoot, 'public/data/catalog.json')
const sorted = values => [...values].sort()
const unique = values => sorted(new Set(values))
const edgeKey = (from, to) => `${from}|${to}`
const grantKey = (object, user, permission, state, column) => JSON.stringify([object, user, permission, state, column ?? null])

// The expected catalog remains an independent contract, never an input used to
// manufacture extraction results. Only a passing live run may refresh the demo.
export function validateExample(catalog, contract) {
  assert.deepEqual(sorted(catalog.nodes.map(n => n.id)), sorted(contract.nodes.map(n => n.id)), 'Catalog identities differ')
  const nodes = new Map(catalog.nodes.map(n => [n.id, n]))
  for (const expected of contract.nodes) {
    const actual = nodes.get(expected.id)
    for (const field of ['server', 'database', 'schema', 'type', 'name']) {
      assert.equal(actual[field] ?? null, expected[field] ?? null, `${expected.id}: ${field}`)
    }
    assert.deepEqual(sorted(actual.columns.map(c => c.name)), sorted(expected.columns), `${expected.id}: columns`)
    // Extractors mask link credentials. Refuse a future regression that exports
    // an actual password; never silently redact and hide the extraction defect.
    for (const match of actual.ddl.matchAll(/(?:IDENTIFIED\s+BY|@rmtpassword\s*=)\s*(?:N)?(?:'([^']*)'|"([^"]*)"|([^\s;]+))/gi)) {
      assert.match(match[1] ?? match[2] ?? match[3], /^[#*]+$/, `${expected.id}: unmasked password`)
    }
  }
  const expectedEdges = contract.dependencies.flatMap(d => d.via
    ? [edgeKey(d.from, d.via), edgeKey(d.via, d.to)] : [edgeKey(d.from, d.to)])
  assert.deepEqual(unique(catalog.edges.map(e => edgeKey(e.from, e.to))), unique(expectedEdges), 'Lineage edges differ')
  assert.deepEqual(sorted(catalog.linkedServerReferences.map(r => `${r.from}|${r.linkedServer}|${r.to}|${!!r.dynamic}`)),
    sorted(contract.dependencies.filter(d => d.via).map(d => `${d.from}|${d.via}|${d.to}|${!!d.dynamic}`)), 'Remote attribution differs')
  assert.deepEqual(catalog.orphanedReferences ?? [], [], 'Unresolved references')
  const users = new Set(contract.users.map(u => u.name))
  assert.deepEqual(sorted(catalog.nodes.flatMap(n => n.grants.filter(g => users.has(g.grantee))
    .map(g => grantKey(n.id, g.grantee, g.permission, g.state, g.column)))),
  sorted(contract.grants.map(g => grantKey(g.object, g.user, g.permission, g.state, g.column))), 'Workload grants differ')
  const semanticEdges = new Set(contract.dependencies.map(d => edgeKey(d.from, d.to)))
  for (const route of contract.paths) {
    route.nodes.forEach(id => assert.ok(nodes.has(id), `Missing path object: ${id}`))
    for (let i = 1; i < route.nodes.length; i++) assert.ok(semanticEdges.has(edgeKey(route.nodes[i - 1], route.nodes[i])), route.name)
  }
  if (catalog.example) {
    assert.equal(catalog.example.scenario, contract.name)
    assert.deepEqual(catalog.example.paths, contract.paths)
    assert.deepEqual(catalog.example.users, contract.users)
  }
}

export function prepareExample(catalog, contract, report) {
  assert.equal(report.scenario, 'heterogeneous-lineage', 'Select a heterogeneous benchmark run')
  assert.equal(report.passed, true, 'The live benchmark must pass')
  assert.equal(report.gatewayRequested, true, 'Run the benchmark with --gateway / -Gateway')
  assert.match(report.oracleToSqlServerExecution, /^passed:/, 'Gateway execution must pass')
  validateExample(catalog, contract)
  return { ...catalog, example: {
    scenario: contract.name,
    title: 'Helios, Atlas & Meridian',
    gatewayVerified: true,
    users: contract.users,
    paths: contract.paths,
  } }
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const read = async file => JSON.parse(await readFile(file, 'utf8'))
  const contract = await read(contractFile)
  if (process.argv[2] === '--check') {
    const catalog = await read(catalogFile)
    validateExample(catalog, contract)
    assert.equal(catalog.example?.gatewayVerified, true, 'Missing verified example metadata')
    console.log(`Verified benchmark example: ${catalog.nodes.length} objects, ${contract.users.length} users, ${contract.paths.length} paths.`)
  } else {
    assert.ok(process.argv[2], 'Usage: node scripts/catalog-example.mjs <successful gateway run directory> | --check')
    const runRoot = path.resolve(process.argv[2])
    const example = prepareExample(await read(path.join(runRoot, 'extracted/catalog.json')), contract, await read(path.join(runRoot, 'benchmark.json')))
    await writeFile(catalogFile, JSON.stringify(example, null, 2) + '\n')
    console.log(`Updated ${catalogFile} from a verified live gateway benchmark.`)
  }
}
