import { fireEvent, render, screen, within } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { describe, expect, it, vi } from 'vitest'
import ObjectPage from './ObjectPage'
import { buildIndex } from '../lib/catalog'
import type { Catalog, CatalogNode } from '../types'

function node(partial: Partial<CatalogNode> & Pick<CatalogNode, 'id' | 'name' | 'type'>): CatalogNode {
  return {
    server: 'SQLPROD01',
    database: 'AppDb',
    schema: 'dbo',
    qualifiedName: `dbo.${partial.name}`,
    path: `${partial.id}.sql`,
    ddl: '-- ddl',
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
const proc = node({
  id: 'SQLPROD01/AppDb/StoredProcedures/dbo/GetOrder', name: 'GetOrder', type: 'StoredProcedures',
  ddl: 'select id, amount from dbo.Orders;',
  sections: [{ title: 'Permissions', content: 'GRANT SELECT ON dbo.Orders TO reader;' }],
  history: [{ sha: 'oldsha1', date: '2026-01-01', message: 'Old revision', ddl: 'select id from dbo.Orders;' }],
})
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
    { linkedServer: link.id, from: proc.id, to: remote.id, database: 'SalesDb', schema: 'dbo', name: 'Orders' },
    { linkedServer: link.id, from: proc.id, to: null, database: 'SalesDb', schema: 'dbo', name: 'Archive' },
  ],
} as unknown as Catalog

vi.mock('../lib/CatalogContext', () => ({
  useCatalog: () => ({ loading: false, error: null, index: buildIndex(catalog) }),
}))

function renderObject(id: string) {
  render(
    <MemoryRouter initialEntries={[`/object/${id}`]}>
      <Routes>
        <Route path="/object/*" element={<ObjectPage />} />
      </Routes>
    </MemoryRouter>,
  )
  fireEvent.click(screen.getByRole('tab', { name: 'Graph' }))
}

describe('ObjectPage linked-server sections', () => {
  it("lists everything referenced through a linked server, with the objects that reach it", () => {
    renderObject(link.id)

    const table = screen.getByRole('table')
    expect(within(table).getAllByRole('columnheader').map(header => header.textContent)).toEqual(['Referenced by', 'Remote object', 'In catalog'])
    const firstRow = within(table).getAllByRole('row')[1]
    expect(within(firstRow).getAllByRole('cell').map(cell => cell.textContent)).toEqual(['dbo.GetOrder', 'SalesDb.dbo.Archive', 'not extracted'])
    expect(within(screen.getByRole('tabpanel', { name: 'Graph' })).getAllByRole('heading', { level: 3 }).slice(0, 2).map(heading => heading.textContent)).toEqual(['Used by (0)', 'Depends on (0)'])
    expect(within(table).getByRole('link', { name: 'SalesDb.dbo.Orders' })).toHaveAttribute(
      'href',
      `/object/${remote.id}`,
    )
    // The target nobody extracted is still listed, just not linkable.
    expect(within(table).getByText('SalesDb.dbo.Archive')).toBeInTheDocument()
    expect(within(table).getAllByRole('link', { name: 'dbo.GetOrder' })).toHaveLength(2)
  })

  it('shows a referencing object which link its remote references go through', () => {
    renderObject(proc.id)

    expect(screen.getByText('References across linked servers')).toBeInTheDocument()
    expect(screen.getAllByRole('link', { name: 'SALES_LINK' })).toHaveLength(2)
  })
})

describe('ObjectPage definition views', () => {
  it('toggles current SQL, sections and historical revisions without changing the source', () => {
    renderObject(proc.id)
    const definition = screen.getByRole('region', { name: 'Definition' })
    const code = () => definition.querySelector('code')?.textContent
    expect(screen.getByRole('button', { name: 'Original' })).toHaveAttribute('aria-pressed', 'true')
    expect(code()).toBe(proc.ddl)
    fireEvent.click(screen.getByRole('button', { name: 'Formatted' }))
    expect(screen.getByRole('button', { name: 'Formatted' })).toHaveAttribute('aria-pressed', 'true')
    expect(code()).toMatch(/select\n/)
    expect(definition.querySelectorAll('.code-block--formatted')).toHaveLength(2)
    expect(proc.ddl).toBe('select id, amount from dbo.Orders;')

    fireEvent.click(screen.getByRole('tab', { name: 'History' }))
    fireEvent.click(screen.getByRole('button', { name: /Old revision/ }))
    expect(code()).toMatch(/select\n\s+id\n/)
    expect(code()).not.toContain('amount')
    fireEvent.click(screen.getByRole('button', { name: 'Original' }))
    expect(code()).toBe(proc.history[0].ddl)
    fireEvent.click(screen.getByRole('button', { name: 'Back to latest' }))
    expect(code()).toBe(proc.ddl)
    expect(definition.querySelectorAll('code')[1].textContent).toBe(proc.sections[0].content)
  })
})
