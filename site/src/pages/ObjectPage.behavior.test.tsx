import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { beforeEach, expect, it, vi } from 'vitest'
import ObjectPage from './ObjectPage'
import { buildIndex, type CatalogIndex } from '../lib/catalog'
import { makeCatalog, makeEdge, makeNode } from '../test/fixtures'
import type { CatalogNode } from '../types'
import { downloadXlsx } from '../lib/xlsx'
let index: CatalogIndex | null
let loading = false
let error: string | undefined
vi.mock('../lib/CatalogContext', () => ({ useCatalog: () => ({ index }) }))
vi.mock('../lib/useCatalogData', () => ({ useCatalogSelection: () => ({ index, loading, error }) }))
vi.mock('../components/LineageGraph', () => ({ default: () => <div>Graph</div> }))
vi.mock('../lib/xlsx', async (original) => ({
  ...(await original<typeof import('../lib/xlsx')>()),
  downloadXlsx: vi.fn(),
}))
beforeEach(() => {
  loading = false
  error = undefined
  vi.clearAllMocks()
})
const tab = (name: string) => fireEvent.click(screen.getByRole('tab', { name }))
const panel = () => within(screen.getByRole('tabpanel'))
function View({ path = '/object/a' }: { path?: string }) {
  return (
    <MemoryRouter initialEntries={[path]}>
      <Routes>
        <Route path="/object/*" element={<ObjectPage />} />
        <Route path="/plain" element={<ObjectPage />} />
      </Routes>
    </MemoryRouter>
  )
}
it('handles a route with no object identifier', () => {
  index = buildIndex(makeCatalog())
  render(<View path="/plain" />)
  expect(screen.getByRole('heading', { name: 'Not found' })).toBeVisible()
})
it('shows singular column usage and toggles its disclosure', () => {
  index = buildIndex(
    makeCatalog({
      nodes: [
        makeNode({ id: 'a', columns: [{ name: 'Id', dataType: 'INT', description: null }] }),
        makeNode({ id: 'b' }),
      ],
      edges: [makeEdge('b', 'a', ['Id'])],
      orphanedReferences: [{ from: 'a', name: 'Missing', schema: null }],
    }),
  )
  render(<View />)
  expect(screen.getByText('1 orphaned reference')).toBeVisible()
  const button = screen.getByRole('button', { name: /1 object/ })
  fireEvent.click(button)
  expect(screen.getByText(/1 object reads/)).toBeVisible()
  fireEvent.click(button)
  expect(screen.queryByRole('heading', { name: 'Lineage for Id' })).not.toBeInTheDocument()
})
it('explains an empty connection target list', () => {
  index = buildIndex(makeCatalog({ nodes: [makeNode({ id: 'a', type: 'DatabaseLinks' })] }))
  render(<View />)
  tab('Graph')
  expect(screen.getByText(/No object in the catalog references/)).toBeVisible()
})
it('handles absent catalog, loading, failures and unknown objects', () => {
  index = null
  const { rerender, container } = render(<View />)
  expect(container).toBeEmptyDOMElement()
  loading = true
  rerender(<View />)
  expect(screen.getByRole('status')).toHaveTextContent('Loading')
  loading = false
  error = 'offline'
  rerender(<View />)
  expect(screen.getByRole('alert')).toHaveTextContent('offline')
  error = undefined
  index = buildIndex(makeCatalog())
  rerender(<View />)
  expect(screen.getByRole('heading', { name: 'Not found' })).toBeVisible()
})
it('exports workbook details, describes grants and metrics, and closes column lineage', async () => {
  const node = makeNode({
    id: 'a',
    description: 'Orders table',
    lastChangedAt: '2026-01-01',
    columns: [{ name: 'Id', dataType: null, description: 'Identifier' }],
    grants: [
      { grantee: 'reader', granteeType: 'USER', permission: 'SELECT', state: 'GRANT', column: null },
      { grantee: 'blocked', granteeType: null, permission: 'UPDATE', state: 'DENY', column: 'Id' },
    ],
    metrics: [
      {
        capturedAt: '2026-01-01',
        rowCount: 1,
        reservedKB: null,
        dataKB: null,
        indexKB: null,
        indexes: [],
        statistics: [],
      },
    ],
  })
  node.actualServerName = 'physical'
  index = buildIndex(
    makeCatalog({
      nodes: [node, makeNode({ id: 'b' }), makeNode({ id: 'c' })],
      edges: [makeEdge('b', 'a', ['Id']), makeEdge('c', 'a', ['Id'])],
      orphanedReferences: [
        { from: 'a', name: 'Missing', schema: null },
        { from: 'a', name: 'Other', server: 'S', database: 'D', schema: 'dbo' },
      ],
    }),
  )
  render(<View />)
  expect(screen.getByText('(actual server: physical)')).toBeVisible()
  expect(screen.getByText('2 orphaned references')).toBeVisible()
  fireEvent.click(screen.getByRole('button', { name: 'Export XLSX' }))
  await waitFor(() => expect(downloadXlsx).toHaveBeenCalled())
  fireEvent.click(screen.getByRole('button', { name: /2 objects/ }))
  expect(screen.getByRole('heading', { name: 'Lineage for Id' })).toBeVisible()
  fireEvent.click(screen.getByRole('button', { name: 'Close' }))
  expect(screen.queryByRole('heading', { name: 'Lineage for Id' })).not.toBeInTheDocument()
  tab('Access')
  expect(panel().getByRole('link', { name: 'blocked' })).toHaveAttribute('href', '/lineage?tab=access&grantee=blocked')
  expect(panel().getByText('DENY')).toHaveClass('grant-state--deny')
  tab('Metrics')
  expect(panel().getByRole('heading', { name: 'Volume' })).toBeVisible()
})
it('renders complete and partial cross-link evidence with multiple callers', () => {
  const node = makeNode({ id: 'a', type: 'DatabaseLinks', schema: null })
  index = buildIndex(
    makeCatalog({
      nodes: [node, makeNode({ id: 'caller' })],
      systemReferences: [
        { from: 'a', database: 'master', schema: 'sys', name: 'objects' },
        { from: 'a', schema: null, name: 'system' },
      ],
      linkedServerReferences: [
        { from: 'caller', linkedServer: 'a', to: 'remote', name: 'Target', schema: 'dbo' },
        { from: 'unknownCaller', linkedServer: 'a', to: 'remote', name: 'Target', schema: 'dbo' },
        {
          from: 'a',
          linkedServer: 'missingLink',
          to: null,
          name: 'Unresolved',
          schema: null,
          status: 'ambiguous',
          dataSource: 'host',
          targetEngine: 'oracle',
          dynamic: true,
        },
        { from: 'a', linkedServer: 'missingLink', to: null, name: 'Other', schema: null, dataSource: 'otherhost' },
      ],
    }),
  )
  render(<View />)
  tab('Graph')
  expect(screen.getByRole('heading', { name: 'Referenced through this database link' })).toBeVisible()
  expect(screen.getByRole('link', { name: 'unknownCaller' })).toBeVisible()
  expect(screen.getByText('(ambiguous destination)')).toBeVisible()
  expect(screen.getByText('— host (oracle)')).toBeVisible()
  expect(screen.getByText('dynamic')).toBeVisible()
})
it('updates revision selections as catalog history changes and handles unavailable compare content', () => {
  let node: CatalogNode = makeNode({
    id: 'a',
    changeCount: 1,
    history: [
      { sha: 'old', date: '2025-01-01', message: 'Old', ddl: 'old' },
      { sha: 'new', date: '2026-01-01', message: 'New', ddl: 'new' },
    ],
  })
  index = buildIndex(makeCatalog({ nodes: [node] }))
  const view = render(<View />)
  tab('Diff')
  expect(panel().getByText('Enable comparison and select two available revisions below.')).toBeVisible()
  tab('History')
  fireEvent.click(panel().getByRole('button', { name: /Old/ }))
  fireEvent.click(panel().getByRole('button', { name: /Current definition/ }))
  expect(screen.queryByRole('status')).not.toBeInTheDocument()
  fireEvent.click(panel().getByRole('button', { name: 'Compare two revisions' }))
  fireEvent.click(panel().getByRole('button', { name: 'Cancel comparison' }))
  expect(screen.getByRole('tab', { name: 'History' })).toHaveAttribute('aria-selected', 'true')
  fireEvent.click(panel().getByRole('button', { name: 'Compare two revisions' }))
  fireEvent.click(panel().getByRole('button', { name: /Old/ }))
  fireEvent.click(panel().getByRole('button', { name: /Old/ }))
  fireEvent.click(panel().getByRole('button', { name: /Old/ }))
  fireEvent.click(panel().getByRole('button', { name: /New/ }))
  expect(panel().getByRole('heading', { name: 'Diff' })).toBeVisible()
  node = { ...node, history: [{ ...node.history[0], ddl: null }, node.history[1]] }
  index = buildIndex(makeCatalog({ nodes: [node] }))
  view.rerender(<View />)
  expect(panel().getByText('Content not available for one of the selected revisions.')).toBeVisible()
  node = { ...node, history: [] }
  index = buildIndex(makeCatalog({ nodes: [node] }))
  view.rerender(<View />)
  expect(panel().getByText('Content not available for one of the selected revisions.')).toBeVisible()
})
