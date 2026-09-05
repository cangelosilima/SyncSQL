import { render, screen, within } from '@testing-library/react'
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
    { linkedServer: link.id, from: proc.id, to: remote.id, database: 'SalesDb', schema: 'dbo', name: 'Orders' },
    { linkedServer: link.id, from: proc.id, to: null, database: 'SalesDb', schema: 'dbo', name: 'Archive' },
  ],
} as unknown as Catalog

vi.mock('../lib/CatalogContext', () => ({
  useCatalog: () => ({ loading: false, error: null, index: buildIndex(catalog) }),
}))

function renderObject(id: string) {
  render(
    <MemoryRouter future={{ v7_startTransition: true, v7_relativeSplatPath: true }} initialEntries={[`/object/${id}`]}>
      <Routes>
        <Route path="/object/*" element={<ObjectPage />} />
      </Routes>
    </MemoryRouter>,
  )
}

describe('ObjectPage linked-server sections', () => {
  it("lists everything referenced through a linked server, with the objects that reach it", () => {
    renderObject(link.id)

    const table = screen.getByRole('table')
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
