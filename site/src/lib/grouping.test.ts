import { describe, expect, it } from 'vitest'
import { buildIndex } from './catalog'
import { countByType, groupRelated, NO_VALUE_LABEL, resolveNodes, searchNodes } from './grouping'
import { bundleNeighborhood } from './neighborhood'
import type { Catalog, CatalogEdge, CatalogNode } from '../types'

function node(id: string, partial: Partial<CatalogNode> = {}): CatalogNode {
  const name = id.split('/').pop()!
  return {
    id,
    server: 'SQLPROD01',
    database: 'AppDb',
    schema: 'dbo',
    type: 'Tables',
    name,
    qualifiedName: `dbo.${name}`,
    path: `${id}.sql`,
    ddl: '-- ddl',
    description: null,
    columns: [],
    grants: [],
    sections: [],
    sizeBytes: 0,
    changeCount: 0,
    lastChangedAt: null,
    history: [],
    metrics: [],
    ...partial,
  }
}

function catalogOf(nodes: CatalogNode[], edges: CatalogEdge[] = []): Catalog {
  return {
    generatedAt: '2026-06-01T00:00:00Z',
    servers: [...new Set(nodes.map((n) => n.server))],
    typeCounts: {},
    nodes,
    edges,
    recentChanges: [],
    coChangePairs: [],
  }
}

describe('grouping', () => {
  const nodes = [
    node('SQLPROD01/AppDb/Tables/dbo/Orders'),
    node('SQLPROD01/AppDb/Tables/dbo/Customers'),
    node('SQLPROD01/AppDb/Views/dbo/OrderSummary', { type: 'Views' }),
    node('SQLPROD02/SalesDb/Tables/sales/Invoices', { server: 'SQLPROD02', database: 'SalesDb', schema: 'sales' }),
    node('SQLPROD01/_ServerLevel/LinkedServers/SALES_LINK', {
      type: 'LinkedServers',
      database: '_ServerLevel',
      schema: null,
      qualifiedName: 'SALES_LINK',
    }),
  ]

  it('orders resolved nodes by server, then database, then name - and drops ids the catalog lost', () => {
    const index = buildIndex(catalogOf(nodes))
    const resolved = resolveNodes(index, [
      'SQLPROD02/SalesDb/Tables/sales/Invoices',
      'nope/missing/Tables/dbo/Gone',
      'SQLPROD01/AppDb/Tables/dbo/Orders',
      'SQLPROD01/AppDb/Tables/dbo/Customers',
    ])

    expect(resolved.map((n) => n.name)).toEqual(['Customers', 'Orders', 'Invoices'])
  })

  it('groups biggest bucket first so the summary leads with what the set is mostly made of', () => {
    const groups = groupRelated(nodes, 'type')
    expect(groups.map((g) => [g.key, g.nodes.length])).toEqual([
      ['Tables', 3],
      ['LinkedServers', 1],
      ['Views', 1],
    ])
  })

  it('buckets schema-less objects under an explicit label rather than an empty group name', () => {
    const groups = groupRelated(nodes, 'schema')
    expect(groups.find((g) => g.key === NO_VALUE_LABEL)?.nodes.map((n) => n.name)).toEqual(['SALES_LINK'])
  })

  it('counts by type for the summary chips', () => {
    expect(countByType(nodes)).toEqual([
      { type: 'Tables', count: 3 },
      { type: 'LinkedServers', count: 1 },
      { type: 'Views', count: 1 },
    ])
  })

  it('searches across every part of a node identity, not just its name', () => {
    expect(searchNodes(nodes, 'salesdb').map((n) => n.name)).toEqual(['Invoices'])
    expect(searchNodes(nodes, 'ORD').map((n) => n.name)).toEqual(['Orders', 'OrderSummary'])
    expect(searchNodes(nodes, '  ').length).toBe(nodes.length)
  })
})

describe('bundleNeighborhood', () => {
  const hub = node('SQLPROD01/AppDb/Views/dbo/Hub', { type: 'Views' })
  const tables = Array.from({ length: 20 }, (_, i) => node(`SQLPROD01/AppDb/Tables/dbo/T${String(i).padStart(2, '0')}`))
  const procs = Array.from({ length: 2 }, (_, i) => node(`SQLPROD01/AppDb/StoredProcedures/dbo/P${i}`, { type: 'StoredProcedures' }))
  // The hub reads every table; the two procedures read the hub.
  const edges: CatalogEdge[] = [
    ...tables.map((t) => ({ from: hub.id, to: t.id, columns: [] })),
    ...procs.map((p) => ({ from: p.id, to: hub.id, columns: [] })),
  ]
  const index = buildIndex(catalogOf([hub, ...tables, ...procs], edges))
  const allIds = [hub.id, ...tables.map((t) => t.id), ...procs.map((p) => p.id)]

  it('leaves a neighborhood that already fits completely alone', () => {
    const result = bundleNeighborhood(index, hub.id, allIds, { maxNodes: 100, maxPerGroup: 5 })
    expect(result.bundles).toEqual([])
    expect(result.bundledCount).toBe(0)
    expect(result.nodeIds).toEqual(allIds)
  })

  it('collapses the big same-type group and keeps the small one whole', () => {
    const result = bundleNeighborhood(index, hub.id, allIds, { maxNodes: 10, maxPerGroup: 8 })

    expect(result.bundles).toHaveLength(1)
    const [bundle] = result.bundles
    expect(bundle.type).toBe('Tables')
    expect(bundle.direction).toBe('outgoing')
    expect(bundle.memberIds).toHaveLength(20)
    expect(result.bundledCount).toBe(20)
    // Focus plus the two procedures, which were too few to be worth collapsing.
    expect(result.nodeIds.sort()).toEqual([hub.id, ...procs.map((p) => p.id)].sort())
  })

  it('never collapses a group small enough that the bundle would hide more than it saves', () => {
    const twoTables = [hub.id, tables[0].id, tables[1].id, ...procs.map((p) => p.id)]
    const result = bundleNeighborhood(index, hub.id, twoTables, { maxNodes: 2, maxPerGroup: 1 })
    expect(result.bundles).toEqual([])
    expect(result.nodeIds.sort()).toEqual(twoTables.sort())
  })

  it('leaves objects that are not direct neighbours of the focus alone', () => {
    const stranger = node('SQLPROD01/AppDb/Tables/dbo/Unrelated')
    const withStranger = buildIndex(catalogOf([hub, ...tables, ...procs, stranger], edges))
    const result = bundleNeighborhood(withStranger, hub.id, [...allIds, stranger.id], { maxNodes: 10, maxPerGroup: 8 })

    expect(result.nodeIds).toContain(stranger.id)
    expect(result.bundles.flatMap((b) => b.memberIds)).not.toContain(stranger.id)
  })
})
