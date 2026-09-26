import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { beforeEach, expect, it, vi } from 'vitest'
import { buildIndex, type CatalogIndex } from '../lib/catalog'
import { makeCatalog, makeEdge, makeNode } from '../test/fixtures'
import type { PartitionedCatalog } from '../lib/partitionedCatalog'
import Home from './Home'
import History from './History'
import Alerts from './Alerts'
import Explorer from './Explorer'

let index: CatalogIndex | null
vi.mock('../lib/CatalogContext', () => ({ useCatalog: () => ({ index }) }))
beforeEach(() => {
  index = buildIndex(makeCatalog())
})
it('shows an empty overview without orphan analysis', () => {
  render(
    <MemoryRouter>
      <Home />
    </MemoryRouter>,
  )
  expect(screen.getByText('0 anomalies · 0 orphaned references')).toBeVisible()
})
it.each([Home, History, Alerts, Explorer])('renders nothing without a catalog (%s)', (Page) => {
  index = null
  expect(
    render(
      <MemoryRouter>
        <Page />
      </MemoryRouter>,
    ).container,
  ).toBeEmptyDOMElement()
})
it('shows overview rankings, changes, and co-change links including missing historical objects', () => {
  index = buildIndex(
    makeCatalog({
      nodes: [
        makeNode({ id: 'a', lastChangedAt: '2026-01-01', changeCount: 1 }),
        makeNode({ id: 'b', type: 'Views', lastChangedAt: '2026-01-02', changeCount: 2 }),
        makeNode({ id: 'login', database: '_ServerLevel' }),
      ],
      edges: [makeEdge('b', 'a')],
      typeCounts: { Tables: 2, Views: 1 },
      recentChanges: [{ sha: 'a', date: '2026-01-01', message: 'change', objectIds: ['a'] }],
      coChangePairs: [
        { a: 'a', b: 'b', count: 2 },
        { a: 'gone', b: 'missing', count: 1 },
      ],
    }),
  )
  index.catalog.edgeCount = 10
  index.catalog.orphanedReferenceCount = 3
  render(
    <MemoryRouter>
      <Home />
    </MemoryRouter>,
  )
  expect(screen.getByText('1 direct · 1 indirect')).toBeVisible()
  expect(screen.getByText('1 change')).toBeVisible()
  expect(screen.getByText('2 changes')).toBeVisible()
  expect(screen.getByText('gone ↔ missing')).toBeVisible()
  expect(screen.getByText('0 anomalies · 3 orphaned references')).toBeVisible()
})
it('renders absent history and singular commits with schema-less objects', () => {
  const { rerender } = render(
    <MemoryRouter>
      <History />
    </MemoryRouter>,
  )
  expect(screen.getByText('No history was mined for this run.')).toBeVisible()
  index = buildIndex(
    makeCatalog({
      nodes: [makeNode({ id: 'a', schema: null })],
      recentChanges: [{ sha: 'a', date: '2026-01-01', message: 'single', objectIds: ['a'] }],
    }),
  )
  rerender(
    <MemoryRouter>
      <History />
    </MemoryRouter>,
  )
  expect(screen.getByText(/^1 commit touching/)).toBeVisible()
  fireEvent.click(screen.getByRole('button', { name: /single/ }))
  expect(screen.getByRole('link', { name: 'SRV1.AppDb..a' })).toBeVisible()
})
it('explains absent alert evidence and unavailable source objects, and clears filters', () => {
  const { rerender } = render(
    <MemoryRouter>
      <Alerts />
    </MemoryRouter>,
  )
  expect(screen.getByText('No alerts detected from the available catalog evidence.')).toBeVisible()
  expect(screen.getByText('Orphaned-reference analysis is not included in this snapshot.')).toBeVisible()
  index = buildIndex(makeCatalog({ orphanedReferences: [{ from: 'gone', name: 'target', schema: null }] }))
  rerender(
    <MemoryRouter>
      <Alerts />
    </MemoryRouter>,
  )
  expect(screen.getByText('Source object unavailable')).toBeVisible()
  fireEvent.change(screen.getByLabelText('Category'), { target: { value: 'orphans' } })
  fireEvent.change(screen.getByLabelText('Category'), { target: { value: 'all' } })
  fireEvent.change(screen.getByLabelText('Search alerts'), { target: { value: 'gone' } })
  fireEvent.change(screen.getByLabelText('Search alerts'), { target: { value: '' } })
  expect(screen.getByRole('status')).toHaveTextContent('1 of 1')
})
it('shows loading and failure when fetching alert partitions', async () => {
  index!.source = { alerts: vi.fn().mockRejectedValue(new Error('Offline')) } as unknown as PartitionedCatalog
  render(
    <MemoryRouter>
      <Alerts />
    </MemoryRouter>,
  )
  expect(screen.getByRole('status')).toHaveTextContent('Loading')
  expect(await screen.findByRole('alert')).toHaveTextContent('Offline')
})
it('sorts every explorer field, renders aliases, and toggles both directions', () => {
  const a = makeNode({ id: 'a', schema: null, description: 'Description', lastChangedAt: '2026-01-01' })
  a.actualServerName = 'actual'
  const b = makeNode({ id: 'b', server: 'B', database: 'B', type: 'Views' })
  b.actualServerName = 'B'
  index = buildIndex(makeCatalog({ nodes: [b, a, makeNode({ id: 'c' })] }))
  render(
    <MemoryRouter>
      <Explorer />
    </MemoryRouter>,
  )
  expect(screen.getByText('(actual)')).toBeVisible()
  for (const label of ['Type', 'Server', 'Database', 'Schema', 'Last changed', 'Object']) {
    fireEvent.click(screen.getByRole('button', { name: new RegExp(`^${label}`) }))
    fireEvent.click(screen.getByRole('button', { name: new RegExp(`^${label}`) }))
    expect(screen.getByRole('columnheader', { name: new RegExp(`^${label}`) })).toHaveAttribute(
      'aria-sort',
      'descending',
    )
    fireEvent.click(screen.getByRole('button', { name: new RegExp(`^${label}`) }))
    expect(screen.getByRole('columnheader', { name: new RegExp(`^${label}`) })).toHaveAttribute(
      'aria-sort',
      'ascending',
    )
  }
})
it('shows search loading and failure states without reporting false empty results', async () => {
  index!.source = { search: vi.fn().mockRejectedValue(new Error('Search offline')) } as unknown as PartitionedCatalog
  render(
    <MemoryRouter initialEntries={['/explorer?q=needle']}>
      <Explorer />
    </MemoryRouter>,
  )
  expect(screen.getByText('Searching the current filter.')).toBeVisible()
  await waitFor(() => expect(screen.getByRole('alert')).toHaveTextContent('Search offline'))
  expect(screen.getByText('Search unavailable the current filter.')).toBeVisible()
  expect(screen.queryByText('No objects match this filter.')).not.toBeInTheDocument()
})
