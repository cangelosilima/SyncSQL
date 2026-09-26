import { render, screen } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { expect, it, vi } from 'vitest'
import ServerPage from './ServerPage'
import { buildIndex, type CatalogIndex } from '../lib/catalog'
import { makeCatalog, makeNode } from '../test/fixtures'
let base: CatalogIndex | null = null
let selection: { index: CatalogIndex | null; loading: boolean; error?: string } = { index: null, loading: false }
vi.mock('../lib/CatalogContext', () => ({ useCatalog: () => ({ index: base }) }))
vi.mock('../lib/useCatalogData', () => ({ useCatalogSelection: () => selection }))
function view(path = '/server/SRV1') {
  return render(
    <MemoryRouter initialEntries={[path]}>
      <Routes>
        <Route path="/server/:serverName?" element={<ServerPage />} />
      </Routes>
    </MemoryRouter>,
  )
}
it('handles loading, failures, absent selection, and missing server metadata', () => {
  selection = { index: null, loading: true }
  const loading = view('/server')
  expect(screen.getByRole('status')).toBeVisible()
  loading.unmount()
  selection = { index: null, loading: false, error: 'Offline' }
  const error = view()
  expect(screen.getByRole('alert')).toHaveTextContent('Offline')
  error.unmount()
  selection = { index: null, loading: false }
  const empty = view()
  expect(empty.container).toBeEmptyDOMElement()
  empty.unmount()
  base = buildIndex(makeCatalog())
  selection = { index: base, loading: false }
  view()
  expect(screen.getByText('Hostname not recorded')).toBeVisible()
  expect(screen.getByText('No custom tags')).toBeVisible()
  expect(screen.getByText('No cross-server relationships are recorded.')).toBeVisible()
  expect(screen.getAllByText('None recorded.')).toHaveLength(2)
})
it('shows metadata and deduplicated incoming and outgoing cross-server relationships', () => {
  base = buildIndex(
    makeCatalog({
      nodes: [
        makeNode({ id: 'link', type: 'LinkedServers', database: '_ServerLevel' }),
        makeNode({ id: 'dbLink', type: 'DatabaseLinks' }),
        makeNode({ id: 'local' }),
        makeNode({ id: 'local2', database: 'OtherDb' }),
        makeNode({ id: 'remote', server: 'REMOTE' }),
      ],
      serverDetails: [{ name: 'srv1', hostname: 'host', environment: 'Production', tags: ['critical'] }],
      linkedServerReferences: [
        { linkedServer: 'link', from: 'local', to: 'remote', name: 'Remote', database: 'Db', schema: 'dbo' },
        { linkedServer: 'link', from: 'local', to: 'remote', name: 'Remote', database: 'Db', schema: 'dbo' },
        { linkedServer: 'dbLink', from: 'local', name: 'Unresolved', schema: null },
        { linkedServer: 'link', from: 'local', to: 'missing', name: 'Missing', schema: 'dbo' },
        { linkedServer: 'link', from: 'local', to: 'local', name: 'Self', schema: null },
        { linkedServer: 'remoteLink', from: 'remote', to: 'local', name: 'Local', schema: null },
        { linkedServer: 'remoteLink', from: 'missing', to: 'local', name: 'Unknown', schema: null },
      ],
    }),
  )
  selection = { index: base, loading: false }
  view()
  expect(screen.getByText('host')).toBeVisible()
  expect(screen.getByText('critical')).toBeVisible()
  expect(screen.getByRole('link', { name: 'REMOTE connected server' })).toHaveAttribute('href', '/server/REMOTE')
  expect(screen.getAllByRole('link', { name: 'REMOTE.AppDb.dbo.remote' })).toHaveLength(2)
  expect(screen.getByText('Unresolved')).toBeVisible()
  expect(screen.getByText('dbo.Missing')).toBeVisible()
})
