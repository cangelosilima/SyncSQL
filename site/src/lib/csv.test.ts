import { describe, expect, it } from 'vitest'
import { csvFileName, escapeCsvCell, toCsv } from './csv'
import { buildIndex } from './catalog'
import { columnColumns, dependencyRows, objectColumns } from './catalogCsv'
import type { Catalog, CatalogNode } from '../types'

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

describe('escapeCsvCell', () => {
  it('leaves plain values alone and renders nullish as empty', () => {
    expect(escapeCsvCell('Orders')).toBe('Orders')
    expect(escapeCsvCell(42)).toBe('42')
    expect(escapeCsvCell(null)).toBe('')
    expect(escapeCsvCell(undefined)).toBe('')
  })

  it('quotes and doubles the quotes in values carrying a delimiter, quote or newline', () => {
    expect(escapeCsvCell('a,b')).toBe('"a,b"')
    expect(escapeCsvCell('say "hi"')).toBe('"say ""hi"""')
    expect(escapeCsvCell('line1\nline2')).toBe('"line1\nline2"')
  })

  it('neutralizes a value a spreadsheet would otherwise evaluate as a formula', () => {
    expect(escapeCsvCell('=1+1')).toBe("'=1+1")
    expect(escapeCsvCell('@SUM(A1)')).toBe("'@SUM(A1)")
    expect(escapeCsvCell('-cmd')).toBe("'-cmd")
    // A real negative number is not a formula and must survive unchanged.
    expect(escapeCsvCell('-42')).toBe('-42')
    expect(escapeCsvCell(-42)).toBe('-42')
  })
})

describe('toCsv', () => {
  it('writes a header row from the column definitions, then one CRLF-terminated row per item', () => {
    const csv = toCsv(
      [{ name: 'Orders', rows: 10 }],
      [
        { header: 'Name', value: (r) => r.name },
        { header: 'Rows', value: (r) => r.rows },
      ],
    )
    expect(csv).toBe('Name,Rows\r\nOrders,10\r\n')
  })
})

describe('csvFileName', () => {
  it('turns an object id into something a filesystem accepts', () => {
    expect(csvFileName('SQLPROD01/AppDb/Tables/dbo/Orders', 'columns')).toBe(
      'SQLPROD01-AppDb-Tables-dbo-Orders-columns.csv',
    )
    expect(csvFileName('', null, undefined)).toBe('syncsql-export.csv')
  })
})

describe('catalog CSV column sets', () => {
  const orders = node('SQLPROD01/AppDb/Tables/dbo/Orders', {
    description: 'Pedidos, "faturados"',
    columns: [{ name: 'Id', dataType: 'int', description: null }],
  })
  const proc = node('SQLPROD01/AppDb/StoredProcedures/dbo/GetOrder', { type: 'StoredProcedures' })
  const report = node('SQLPROD01/AppDb/Views/dbo/OrderReport', { type: 'Views' })
  const catalog = {
    generatedAt: '2026-06-01T00:00:00Z',
    servers: ['SQLPROD01'],
    typeCounts: {},
    nodes: [orders, proc, report],
    edges: [
      { from: proc.id, to: orders.id, columns: ['Id'] },
      { from: report.id, to: proc.id, columns: [] },
    ],
    recentChanges: [],
    coChangePairs: [],
  } as unknown as Catalog
  const index = buildIndex(catalog)

  it('exports an object with its connectivity counts, quoting text that needs it', () => {
    const csv = toCsv([orders], objectColumns(index))
    const [header, row] = csv.trimEnd().split('\r\n')
    expect(header).toContain('DependsOnCount,UsedByCount')
    expect(row).toContain('"Pedidos, ""faturados"""')
    // Orders depends on nothing and is used by the procedure.
    expect(row.endsWith(',0,1,0,')).toBe(true)
  })

  it('exports both lineage directions as flat rows, dependencies first', () => {
    const rows = dependencyRows(index, proc.id)
    expect(rows.map((r) => [r.direction, r.node.name, r.columns.join(' ')])).toEqual([
      ['Depends on', 'Orders', 'Id'],
      ['Used by', 'OrderReport', ''],
    ])
  })

  it('exports columns with their data type and description', () => {
    expect(toCsv(orders.columns, columnColumns)).toBe('Column,DataType,Description\r\nId,int,\r\n')
  })
})
