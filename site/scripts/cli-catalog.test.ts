// @vitest-environment node
import { afterEach, expect, it, vi } from 'vitest'
import { execFileSync } from 'node:child_process'
import { mkdir, mkdtemp, readFile, rm, writeFile } from 'node:fs/promises'
import { tmpdir } from 'node:os'
import { gunzipSync } from 'node:zlib'
import path from 'node:path'
import { loadPartitionedCatalog, type CatalogManifest } from '../src/lib/partitionedCatalog'
import { partitionCatalog } from './partition-catalog.mjs'
import { buildIndex } from '../src/lib/catalog'
import { getTopReferencedTables } from '../src/lib/analytics'
import { detectMetricAnomalies } from '../src/lib/anomalies'
import type { Catalog } from '../src/types'

afterEach(() => vi.unstubAllGlobals())

// Independently assemble full payloads to compare with the lazy browser loader.
async function readCatalog(manifestPath: string): Promise<Catalog> {
  const manifest: CatalogManifest = JSON.parse(await readFile(manifestPath, 'utf8'))
  const payload = async (file: string) =>
    JSON.parse(gunzipSync(await readFile(path.join(path.dirname(manifestPath), file))).toString())
  const nodes: Catalog['nodes'] = []
  const graphs = []
  for (const part of manifest.partitions) {
    const [details, search, grants, graph] = await Promise.all(
      [part.details, part.search, part.grants, part.edges].map(payload),
    )
    for (const detail of details)
      nodes.push({
        ...detail,
        ...search.find((record: { id: string }) => record.id === detail.id),
        grants: grants.find((record: { id: string }) => record.id === detail.id)?.grants ?? [],
      })
    graphs.push(graph)
  }
  const unique = (field: string) => [
    ...new Map(graphs.flatMap((graph) => graph[field]).map((value) => [JSON.stringify(value), value])).values(),
  ]
  return {
    ...manifest,
    nodes,
    edges: unique('edges'),
    orphanedReferences: unique('orphanedReferences'),
    systemReferences: unique('systemReferences'),
    linkedServerReferences: unique('linkedServerReferences'),
  }
}

// CI supplies the built CLI; Node-only site development can keep using the demo fixture.
it.runIf(process.env.SYNCSQL_CLI)(
  'loads the native CLI consolidated catalog through the production site publisher and client',
  async () => {
    const root = await mkdtemp(path.join(tmpdir(), 'syncsql-cli-contract-'))
    const cli = path.resolve(process.env.SYNCSQL_CLI!)
    const run = (...args: string[]) => execFileSync('dotnet', [cli, 'catalog', 'build', ...args], { cwd: root })
    const object = async (file: string, engine: string, ddl: string) => {
      const destination = path.join(root, file)
      await mkdir(path.dirname(destination), { recursive: true })
      await writeFile(destination, `-- Engine: ${engine}\n\n${ddl}\n`)
    }
    try {
      await object(
        'MSSQL/SQL01/LinkedServers/ORA_LINK.sql',
        'mssql',
        "EXEC sp_addlinkedserver @server=N'ORA_LINK', @srvproduct=N'Oracle', @provider=N'OraOLEDB.Oracle', @datasrc=N'ORA01', @catalog=N'APP';",
      )
      await object(
        'MSSQL/SQL01/Local/Views/dbo/Report.sql',
        'mssql',
        'CREATE VIEW dbo.Report AS SELECT Id FROM ORA_LINK.APP.APP.Orders;',
      )
      await object(
        'ORACLE/ORA01/APP/Tables/APP/Orders.sql',
        'oracle',
        'CREATE TABLE APP.Orders (Id NUMBER);\n\n-- === Columns ===\n-- [col] Id|NUMBER\n\n-- === Grants ===\n-- [grant] SELECT|GRANT|READER|ROLE|',
      )
      const metricsPath = path.join(root, 'ORACLE/metrics/ORA01/APP/Tables/APP/Orders.json')
      await mkdir(path.dirname(metricsPath), { recursive: true })
      await writeFile(
        metricsPath,
        JSON.stringify(
          [100, 500].map((rowCount, day) => ({
            capturedAt: `2026-09-${day + 20}T00:00:00Z`,
            rowCount,
            indexes: [{ name: 'PK_Orders', fragmentationPct: 65 }],
            statistics: [],
          })),
        ),
      )
      execFileSync('git', ['init', '--quiet'], { cwd: root })
      execFileSync('git', ['add', 'MSSQL', 'ORACLE'], { cwd: root })
      execFileSync(
        'git',
        [
          '-c',
          'user.name=Catalog Contract',
          '-c',
          'user.email=catalog@example.invalid',
          '-c',
          'commit.gpgsign=false',
          'commit',
          '--quiet',
          '-m',
          'Both engines',
        ],
        { cwd: root },
      )
      run('--repo-root', root)
      const manifestPath = path.join(root, 'catalog/catalog.json')
      const original = await readCatalog(manifestPath)
      const manifest: CatalogManifest = JSON.parse(await readFile(manifestPath, 'utf8'))
      expect(manifest.format).toBe('syncsql-partitioned')
      expect(manifest.servers).toEqual(['ORA01', 'SQL01'])
      expect(manifest.nodeCount).toBe(3)
      expect(manifest.edgeCount).toBe(2)
      expect(original.linkedServerReferences![0].to).toBe('ORA01/APP/Tables/APP/Orders')
      expect(original.recentChanges[0].objectIds).toHaveLength(3)
      expect(original.coChangePairs).toHaveLength(3)
      expect(original.nodes.every((node) => node.history.length === 1)).toBe(true)
      expect(original.nodes.find((node) => node.engine === 'oracle')!.metrics).toHaveLength(2)
      const output = path.join(root, 'site/data')
      expect(await partitionCatalog(manifestPath, output)).toEqual(manifest)
      vi.stubGlobal(
        'fetch',
        async (url: string) => new Response(await readFile(path.join(output, url.replace('/project/data/', '')))),
      )
      const index = await loadPartitionedCatalog(manifest, '/project/data/')
      expect(index.catalog.nodes.map((n) => n.engine).sort()).toEqual(['mssql', 'mssql', 'oracle'])
      expect(index.catalog.recentChanges).toEqual(original.recentChanges)
      expect(detectMetricAnomalies(index.catalog.nodes)).toEqual(detectMetricAnomalies(original.nodes))
      for (const node of original.nodes) expect(await index.source!.object(node.id)).toEqual(node)
      await index.source!.edges()
      expect(index.catalog.edges).toEqual(original.edges)
      expect(index.catalog.linkedServerReferences).toEqual(original.linkedServerReferences)
      expect(getTopReferencedTables(index).map(({ node, ...usage }) => ({ id: node.id, ...usage }))).toEqual(
        getTopReferencedTables(buildIndex(original)).map(({ node, ...usage }) => ({ id: node.id, ...usage })),
      )
      // A selected engine still works independently, with an unresolved remote target.
      run('--objects-root', 'MSSQL', '--output', 'scoped/catalog.json')
      const scoped = await readCatalog(path.join(root, 'scoped/catalog.json'))
      expect(scoped.nodes).toHaveLength(2)
      expect(scoped.linkedServerReferences![0].to).toBeUndefined()
    } finally {
      await rm(root, { recursive: true, force: true })
    }
  },
  60_000,
)
