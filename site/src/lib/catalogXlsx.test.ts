import { describe, expect, it } from 'vitest'
import { buildIndex } from './catalog'
import { objectWorkbookSheets } from './catalogXlsx'
import { makeCatalog, makeEdge, makeNode } from '../test/fixtures'

const orders = makeNode({
  id: 'orders',
  name: 'Orders',
  qualifiedName: 'dbo.Orders',
  ddl: 'CREATE TABLE dbo.Orders (\n  Id INT,\n  CustomerId INT\n);',
  columns: [
    { name: 'Id', dataType: 'int', description: null },
    { name: 'CustomerId', dataType: 'int', description: 'Who ordered' },
  ],
  grants: [{ permission: 'SELECT', state: 'GRANT', grantee: 'app_reader', granteeType: 'SQL_USER', column: null }],
  sections: [{ title: 'Indexes', content: 'CREATE INDEX IX_Orders ON dbo.Orders (Id);' }],
})
const report = makeNode({ id: 'report', name: 'Report', qualifiedName: 'dbo.Report', type: 'Views' })

const index = buildIndex(
  makeCatalog({
    nodes: [orders, report],
    edges: [makeEdge('report', 'orders', ['Id'])],
    orphanedReferences: [{ from: 'orders', schema: 'dbo', name: 'DroppedTable' }],
    systemReferences: [{ from: 'orders', schema: null, name: 'sp_executesql' }],
  }),
)

function sheetNames(nodeId: string): string[] {
  return objectWorkbookSheets(index, index.byId.get(nodeId)!).map((s) => s.name)
}

describe('objectWorkbookSheets', () => {
  it('gives each section of the object page its own worksheet', () => {
    expect(sheetNames('orders')).toEqual([
      'Details',
      'Columns',
      'Access',
      'Used by',
      'Orphaned refs',
      'System refs',
      'Definition',
      'Sections',
    ])
  })

  it('skips sections the object has nothing in', () => {
    // A workbook full of blank tabs is worse than a short one.
    const names = sheetNames('report')

    expect(names).toContain('Details')
    expect(names).toContain('Depends on')
    expect(names).not.toContain('Columns')
    expect(names).not.toContain('Access')
    expect(names).not.toContain('Used by')
  })

  it('transposes the single-row Details section into property/value pairs', () => {
    const details = objectWorkbookSheets(index, orders)[0]

    expect(details.headers).toEqual(['Property', 'Value'])
    expect(details.rows).toContainEqual(['QualifiedName', 'dbo.Orders'])
    expect(details.rows).toContainEqual(['UsedByCount', 1])
  })

  it('carries the column-level tags into the dependency worksheets', () => {
    const usedBy = objectWorkbookSheets(index, orders).find((s) => s.name === 'Used by')!

    expect(usedBy.headers).toContain('ReferencedColumns')
    expect(usedBy.rows[0]).toContain('Id')
  })

  it('splits the definition one row per line so it is readable in Excel', () => {
    const definition = objectWorkbookSheets(index, orders).find((s) => s.name === 'Definition')!

    expect(definition.headers).toEqual(['Line', 'Text'])
    expect(definition.rows).toHaveLength(4)
    expect(definition.rows[0]).toEqual([1, 'CREATE TABLE dbo.Orders ('])
  })
})
