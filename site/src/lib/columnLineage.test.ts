import { describe, expect, it } from 'vitest'
import { buildIndex } from './catalog'
import { getColumnConsumers, getColumnUsageCount, getColumnUsageCounts } from './columnLineage'
import { makeCatalog, makeEdge, makeNode } from '../test/fixtures'

const orders = makeNode({
  id: 'orders',
  name: 'Orders',
  qualifiedName: 'dbo.Orders',
  columns: [
    { name: 'Id', dataType: 'int', description: null },
    { name: 'CustomerId', dataType: 'int', description: null },
    { name: 'Notes', dataType: 'nvarchar', description: null },
  ],
})
const report = makeNode({ id: 'report', name: 'Report', qualifiedName: 'dbo.Report', type: 'Views' })
const audit = makeNode({ id: 'audit', name: 'Audit', qualifiedName: 'dbo.Audit', type: 'StoredProcedures' })
const unrelated = makeNode({ id: 'unrelated', name: 'Unrelated', qualifiedName: 'dbo.Unrelated' })

const index = buildIndex(
  makeCatalog({
    nodes: [orders, report, audit, unrelated],
    edges: [
      makeEdge('report', 'orders', ['Id', 'CustomerId']),
      makeEdge('audit', 'orders', ['customerid']),
      makeEdge('orders', 'unrelated', ['Id']),
    ],
  }),
)

describe('getColumnConsumers', () => {
  it('returns the objects whose DDL reads the column', () => {
    expect(getColumnConsumers(index, 'orders', 'CustomerId').map((n) => n.id)).toEqual(['audit', 'report'])
  })

  it('matches column names case-insensitively', () => {
    // The edge records the caller's spelling, which need not match the column's own.
    expect(getColumnConsumers(index, 'orders', 'CUSTOMERID').map((n) => n.id)).toEqual(['audit', 'report'])
  })

  it('is empty for a column nothing was seen referencing', () => {
    expect(getColumnConsumers(index, 'orders', 'Notes')).toEqual([])
  })

  /** Outgoing edges carry the *target's* columns, so they say nothing about this object's own. */
  it('ignores outgoing edges that happen to name a same-named column', () => {
    expect(getColumnConsumers(index, 'orders', 'Id').map((n) => n.id)).toEqual(['report'])
  })
})

describe('getColumnUsageCounts', () => {
  it('counts consumers per column', () => {
    const counts = getColumnUsageCounts(index, 'orders')

    expect(getColumnUsageCount(counts, 'CustomerId')).toBe(2)
    expect(getColumnUsageCount(counts, 'Id')).toBe(1)
    expect(getColumnUsageCount(counts, 'Notes')).toBe(0)
  })

  it('counts one consuming object once per column', () => {
    const duplicated = buildIndex(
      makeCatalog({
        nodes: [orders, report],
        edges: [makeEdge('report', 'orders', ['Id', 'id', 'ID'])],
      }),
    )

    expect(getColumnUsageCount(getColumnUsageCounts(duplicated, 'orders'), 'Id')).toBe(1)
  })

  it('is empty for an object nothing depends on', () => {
    expect(getColumnUsageCounts(index, 'report').size).toBe(0)
  })
})
