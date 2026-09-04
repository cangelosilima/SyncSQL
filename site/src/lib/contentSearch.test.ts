import { filterByContent, matchesContentQuery, nodeContentText } from './contentSearch'
import { makeNode } from '../test/fixtures'

describe('nodeContentText', () => {
  it('returns just the DDL when a node has no appended sections', () => {
    const node = makeNode({ id: 'a', ddl: 'CREATE VIEW v AS SELECT 1' })
    expect(nodeContentText(node)).toBe('CREATE VIEW v AS SELECT 1')
  })

  it('appends section content so references inside them are searchable', () => {
    const node = makeNode({
      id: 'a',
      ddl: 'CREATE TABLE Orders (Id INT)',
      sections: [
        { title: 'Foreign Keys', content: 'REFERENCES dbo.Customers (Id)' },
        { title: 'Indexes', content: 'CREATE INDEX IX_Orders_Id ON Orders (Id)' },
      ],
    })
    expect(nodeContentText(node)).toBe(
      'CREATE TABLE Orders (Id INT)\nREFERENCES dbo.Customers (Id)\nCREATE INDEX IX_Orders_Id ON Orders (Id)',
    )
  })
})

describe('matchesContentQuery', () => {
  const node = makeNode({
    id: 'a',
    ddl: 'CREATE TABLE Orders (Id INT)',
    sections: [{ title: 'Foreign Keys', content: 'REFERENCES dbo.Customers (Id)' }],
  })

  it('matches case-insensitively', () => {
    expect(matchesContentQuery(node, 'oRDeRs')).toBe(true)
  })

  it('matches text that only appears in a section', () => {
    expect(matchesContentQuery(node, 'Customers')).toBe(true)
  })

  it('treats a blank or whitespace-only query as a no-op', () => {
    expect(matchesContentQuery(node, '')).toBe(true)
    expect(matchesContentQuery(node, '   ')).toBe(true)
  })

  it('rejects text present in neither DDL nor sections', () => {
    expect(matchesContentQuery(node, 'Invoices')).toBe(false)
  })
})

describe('filterByContent', () => {
  const orders = makeNode({ id: 'orders', ddl: 'CREATE TABLE Orders (Id INT)' })
  const invoices = makeNode({ id: 'invoices', ddl: 'CREATE TABLE Invoices (Id INT)' })
  const nodes = [orders, invoices]

  it('keeps only the matching nodes', () => {
    expect(filterByContent(nodes, 'invoices').map((n) => n.id)).toEqual(['invoices'])
  })

  it('returns the original array untouched for an empty query', () => {
    expect(filterByContent(nodes, '  ')).toBe(nodes)
  })
})
