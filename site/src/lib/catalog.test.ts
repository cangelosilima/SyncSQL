import { describe, expect, it } from 'vitest'
import { buildIndex, isLinkNode, qualifiedRefName } from './catalog'
import type { Catalog, CatalogNode } from '../types'

function node(partial: Partial<CatalogNode> & Pick<CatalogNode, 'id' | 'name' | 'type'>): CatalogNode {
  return {
    server: 'SQLPROD01',
    database: 'AppDb',
    schema: 'dbo',
    qualifiedName: `dbo.${partial.name}`,
    path: `${partial.id}.sql`,
    ddl: '',
    columns: [],
    grants: [],
    sections: [],
    sizeBytes: 0,
    changeCount: 0,
    history: [],
    metrics: [],
    ...partial,
  } as CatalogNode
}

const link = node({
  id: 'SQLPROD01/_ServerLevel/LinkedServers/SALES_LINK',
  name: 'SALES_LINK',
  type: 'LinkedServers',
  database: '_ServerLevel',
  schema: null,
  qualifiedName: 'SALES_LINK',
})
const proc = node({ id: 'SQLPROD01/AppDb/StoredProcedures/dbo/GetOrder', name: 'GetOrder', type: 'StoredProcedures' })
const remote = node({
  id: 'SQLPROD02/SalesDb/Tables/dbo/Orders',
  name: 'Orders',
  type: 'Tables',
  server: 'SQLPROD02',
  database: 'SalesDb',
})

const catalog = {
  generatedAt: '2026-06-01T12:00:00Z',
  servers: ['SQLPROD01', 'SQLPROD02'],
  typeCounts: {},
  nodes: [link, proc, remote],
  edges: [],
  linkedServerReferences: [
    {
      linkedServer: link.id,
      from: proc.id,
      to: remote.id,
      database: 'SalesDb',
      schema: 'dbo',
      name: 'Orders',
    },
    {
      linkedServer: link.id,
      from: proc.id,
      to: null,
      database: 'SalesDb',
      schema: 'dbo',
      name: 'Archive',
    },
  ],
} as unknown as Catalog

describe('linked-server references in the catalog index', () => {
  it('groups every reference under the link it crosses', () => {
    const index = buildIndex(catalog)

    expect(index.linkedServerRefsByLink.get(link.id)?.map((r) => r.name)).toEqual(['Orders', 'Archive'])
  })

  it('also indexes them by the object that makes them', () => {
    const index = buildIndex(catalog)

    expect(index.linkedServerRefsByFrom.get(proc.id)).toHaveLength(2)
    expect(index.linkedServerRefsByFrom.has(remote.id)).toBe(false)
  })

  it('leaves the maps empty for a catalog without the field', () => {
    const index = buildIndex({ ...catalog, linkedServerReferences: undefined })

    expect(index.linkedServerRefsByLink.size).toBe(0)
  })
})

describe('qualifiedRefName', () => {
  it('keeps only the parts the DDL actually wrote', () => {
    expect(qualifiedRefName({ schema: 'dbo', name: 'Orders' })).toBe('dbo.Orders')
    expect(qualifiedRefName({ database: 'SalesDb', schema: 'dbo', name: 'Orders' })).toBe('SalesDb.dbo.Orders')
    expect(qualifiedRefName({ server: 'LNK', database: 'SalesDb', schema: 'dbo', name: 'Orders' })).toBe(
      'LNK.SalesDb.dbo.Orders',
    )
    expect(qualifiedRefName({ schema: null, name: 'Orders' })).toBe('Orders')
  })
})

describe('isLinkNode', () => {
  it('recognizes both engines\' link objects', () => {
    expect(isLinkNode(link)).toBe(true)
    expect(isLinkNode({ ...link, type: 'DatabaseLinks' })).toBe(true)
    expect(isLinkNode(proc)).toBe(false)
  })
})
