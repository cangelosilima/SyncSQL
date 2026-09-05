import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { describe, expect, it, vi } from 'vitest'
import RelatedObjects from './RelatedObjects'
import { buildIndex } from '../lib/catalog'
import type { Catalog, CatalogEdge, CatalogNode } from '../types'

function node(id: string, partial: Partial<CatalogNode> = {}): CatalogNode {
  const name = id.split('/').pop()!
  const schema = partial.schema === undefined ? 'dbo' : partial.schema
  return {
    id,
    server: 'SQLPROD01',
    database: 'AppDb',
    schema,
    type: 'Tables',
    name,
    qualifiedName: schema ? `${schema}.${name}` : name,
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

const hub = node('SQLPROD01/AppDb/StoredProcedures/dbo/Rebuild', { type: 'StoredProcedures' })
const tables = Array.from({ length: 40 }, (_, i) => node(`SQLPROD01/AppDb/Tables/dbo/T${String(i).padStart(2, '0')}`))
const views = Array.from({ length: 3 }, (_, i) =>
  node(`SQLPROD02/SalesDb/Views/sales/V${i}`, { type: 'Views', server: 'SQLPROD02', database: 'SalesDb', schema: 'sales' }),
)
const dependencies = [...tables, ...views]

const catalog = {
  generatedAt: '2026-06-01T00:00:00Z',
  servers: ['SQLPROD01', 'SQLPROD02'],
  typeCounts: {},
  nodes: [hub, ...dependencies],
  edges: dependencies.map((n): CatalogEdge => ({ from: hub.id, to: n.id, columns: [] })),
  recentChanges: [],
  coChangePairs: [],
} as unknown as Catalog

const index = buildIndex(catalog)

vi.mock('../lib/CatalogContext', () => ({
  useCatalog: () => ({ loading: false, error: null, index }),
}))

function renderList(ids: string[]) {
  return render(
    <MemoryRouter future={{ v7_startTransition: true, v7_relativeSplatPath: true }}>
      <RelatedObjects title="Depends on" rootId={hub.id} ids={ids} direction="outgoing" />
    </MemoryRouter>,
  )
}

describe('RelatedObjects', () => {
  it('stays a plain list when there are few enough dependencies to just read', () => {
    renderList(tables.slice(0, 4).map((n) => n.id))

    expect(screen.getByRole('heading', { name: 'Depends on (4)' })).toBeInTheDocument()
    expect(screen.queryByRole('searchbox')).not.toBeInTheDocument()
    expect(screen.getAllByRole('link')).toHaveLength(4)
  })

  it('leads with a per-type summary and collapsible groups once the list is too long to read', () => {
    renderList(dependencies.map((n) => n.id))

    expect(screen.getByRole('heading', { name: 'Depends on (43)' })).toBeInTheDocument()
    // The summary answers "what is all this?" before any name is rendered.
    expect(screen.getByRole('button', { name: 'Filter to Tables (40)' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Filter to Views (3)' })).toBeInTheDocument()
    // The 40-row group is capped rather than dumped in full.
    expect(screen.getByRole('button', { name: 'Show all 40' })).toBeInTheDocument()
    expect(screen.queryByRole('link', { name: 'dbo.T39' })).not.toBeInTheDocument()
  })

  it('expands one group on request without touching the others', async () => {
    const user = userEvent.setup()
    renderList(dependencies.map((n) => n.id))

    await user.click(screen.getByRole('button', { name: 'Show all 40' }))

    expect(screen.getByRole('link', { name: 'dbo.T39' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Show fewer' })).toBeInTheDocument()
  })

  it('narrows to one type when its summary chip is clicked', async () => {
    const user = userEvent.setup()
    renderList(dependencies.map((n) => n.id))

    await user.click(screen.getByRole('button', { name: 'Filter to Views (3)' }))

    expect(screen.getByText(/Showing 3 of 43/)).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'sales.V0' })).toBeInTheDocument()
    expect(screen.queryByRole('link', { name: 'dbo.T00' })).not.toBeInTheDocument()
  })

  it('searches across the whole set, not just the group that happens to be open', async () => {
    const user = userEvent.setup()
    renderList(dependencies.map((n) => n.id))

    await user.type(screen.getByRole('searchbox'), 'salesdb')

    expect(screen.getByText(/Showing 3 of 43/)).toBeInTheDocument()
    expect(screen.getAllByRole('link')).toHaveLength(3)
  })

  it('regroups on a different axis without losing anything', async () => {
    const user = userEvent.setup()
    renderList(dependencies.map((n) => n.id))

    await user.selectOptions(screen.getByLabelText('Group by'), 'server')

    expect(screen.getByText(/grouped into 2 groups/)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Show all 40' })).toBeInTheDocument()
  })
})
