import { describe, expect, it } from 'vitest'
import { buildIndex } from './catalog'
import { objectWorkbookSheets } from './catalogXlsx'
import { makeCatalog, makeEdge, makeNode } from '../test/fixtures'
import { dependencyColumns, dependencyRows, objectColumns } from './catalogCsv'

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
  it('exports linked references, metric snapshots, and versions including unavailable definitions', () => {
    const node = makeNode({
      id: 'link',
      ddl: '',
      history: [
        { sha: 'a', date: '2026-01-01', message: 'created', ddl: 'SELECT 1' },
        { sha: 'b', date: '2026-01-02', message: 'missing', ddl: null },
      ],
      metrics: [
        { capturedAt: '2026-01-01', rowCount: 1, reservedKB: 2, dataKB: 3, indexKB: 4, indexes: [], statistics: [] },
      ],
    })
    const refs = [
      {
        from: 'link',
        linkedServer: 'link',
        to: 'target',
        server: 'Remote',
        database: 'Db',
        schema: 'dbo',
        name: 'T',
        dynamic: true,
      },
      { from: 'link', linkedServer: 'missing', schema: null, name: 'Unknown' },
    ]
    const catalog = buildIndex(
      makeCatalog({
        nodes: [node],
        linkedServerReferences: refs,
        orphanedReferences: [{ from: 'link', server: 'Remote', database: 'Db', schema: null, name: 'Gone' }],
      }),
    )
    const sheets = objectWorkbookSheets(catalog, node)
    expect(sheets.find((s) => s.name === 'Across linked servers')?.rows).toEqual([
      [node.qualifiedName, 'Remote.Db.dbo.T', 'yes', 'target', 'yes'],
      ['missing', 'Unknown', 'not extracted', undefined, 'no'],
    ])
    expect(sheets.find((s) => s.name === 'Through this link')?.rows).toHaveLength(1)
    expect(sheets.find((s) => s.name === 'Metrics')?.rows).toEqual([['2026-01-01', 1, 2, 3, 4, 0, 0]])
    expect(sheets.find((s) => s.name === 'Change history')?.rows).toEqual([
      ['2026-01-01', 'a', 'created', 'yes'],
      ['2026-01-02', 'b', 'missing', 'no'],
    ])
    expect(sheets.some((s) => s.name === 'Definition')).toBe(false)
  })

  it('exports dependency identity and column tags and prefers precomputed summary counts', () => {
    const row = dependencyRows(index, 'report')[0]
    expect(dependencyColumns.map((column) => column.value(row))).toEqual([
      'Depends on',
      'orders',
      'SRV1',
      'AppDb',
      'dbo',
      'Tables',
      'dbo.Orders',
      'Id',
    ])
    const summary = { ...orders, columnCount: 50, grantCount: 20, dependsOnCount: 3, usedByCount: 4 }
    const values = Object.fromEntries(objectColumns(index).map((column) => [column.header, column.value(summary)]))
    expect(values).toMatchObject({ ColumnCount: 50, GrantCount: 20, DependsOnCount: 3, UsedByCount: 4 })
  })
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
