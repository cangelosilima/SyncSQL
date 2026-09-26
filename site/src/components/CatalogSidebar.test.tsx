import { fireEvent, render, screen, within } from '@testing-library/react'
import { MemoryRouter, useLocation } from 'react-router-dom'
import { expect, it, vi } from 'vitest'
import CatalogSidebar from './CatalogSidebar'
import { makeNode } from '../test/fixtures'
import { decodeTokensFromUrl } from '../lib/filters'
function Location() {
  const location = useLocation()
  return (
    <output data-testid="location">
      {location.pathname}
      {location.search}
    </output>
  )
}
it('resizes with pointer capture, stops on all cancellation events, and tolerates disabled storage', () => {
  const get = vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
    throw new Error('disabled')
  })
  const set = vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
    throw new Error('disabled')
  })
  vi.stubGlobal('PointerEvent', MouseEvent)
  const { unmount } = render(
    <MemoryRouter>
      <CatalogSidebar nodes={[]} />
    </MemoryRouter>,
  )
  const resizer = screen.getByRole('separator')
  Object.defineProperty(resizer, 'setPointerCapture', { value: vi.fn() })
  expect(resizer).toHaveAttribute('aria-valuenow', '280')
  fireEvent.pointerMove(resizer, { clientX: 50 })
  for (const end of ['pointerUp', 'pointerCancel', 'lostPointerCapture'] as const) {
    fireEvent.pointerDown(resizer, { clientX: 0 })
    fireEvent.pointerMove(resizer, { clientX: 20 })
    fireEvent[end](resizer)
    const width = resizer.getAttribute('aria-valuenow')
    fireEvent.pointerMove(resizer, { clientX: 100 })
    expect(resizer).toHaveAttribute('aria-valuenow', width)
  }
  expect(resizer).toHaveAttribute('aria-valuenow', '340')
  fireEvent.keyDown(resizer, { key: 'ArrowLeft' })
  expect(resizer).toHaveAttribute('aria-valuenow', '320')
  fireEvent.keyDown(resizer, { key: 'Home' })
  expect(resizer).toHaveAttribute('aria-valuenow', '220')
  fireEvent.keyDown(resizer, { key: 'Escape' })
  unmount()
  get.mockRestore()
  set.mockRestore()
  vi.unstubAllGlobals()
})
it('uses precomputed link engines and tolerates missing remote endpoints and metadata', () => {
  const link = makeNode({ id: 'link', type: 'LinkedServers', database: '_ServerLevel', schema: null })
  link.linkTargetEngines = ['oracle', 'mssql', ' custom ']
  render(
    <MemoryRouter>
      <CatalogSidebar
        nodes={[link, makeNode({ id: 'target' })]}
        linkedServerReferences={[
          { from: 'target', linkedServer: 'link', to: 'target', name: 'T', schema: null },
          { from: 'target', linkedServer: 'link', name: 'missing', schema: null },
          { from: 'target', linkedServer: 'link', to: 'target', name: 'Again', schema: null },
          { from: 'target', linkedServer: 'link', to: 'unknown', name: 'unknown', schema: null },
        ]}
      />
    </MemoryRouter>,
  )
  fireEvent.click(screen.getByRole('button', { name: 'SRV1 2' }))
  fireEvent.click(screen.getByRole('button', { name: 'LinkedServers 1' }))
  expect(screen.getByRole('link', { name: /Oracle, SQL Server, custom/ })).toBeVisible()
})
it('labels link destinations using target metadata, including aliases, without using the host engine', () => {
  const links = ['REMOTE', 'ALIAS', 'UNKNOWN'].map((name) =>
    makeNode({
      id: name,
      name,
      qualifiedName: name,
      server: 'HOST',
      engine: 'mssql',
      database: '_ServerLevel',
      schema: null,
      type: 'LinkedServers',
    }),
  )
  render(
    <MemoryRouter>
      <CatalogSidebar
        nodes={[...links, makeNode({ id: 'target', server: 'remote', engine: 'oracle' })]}
        linkedServerReferences={[{ linkedServer: 'ALIAS', from: 'caller', to: 'target', schema: null, name: 'target' }]}
      />
    </MemoryRouter>,
  )
  fireEvent.click(screen.getByRole('button', { name: /HOST/ }))
  fireEvent.click(screen.getByRole('button', { name: /LinkedServers/ }))
  expect(screen.getByRole('link', { name: 'REMOTE (Oracle)' })).toHaveAttribute('href', '/object/REMOTE')
  expect(screen.getByRole('link', { name: 'ALIAS (Oracle)' })).toBeVisible()
  expect(screen.getByRole('link', { name: 'UNKNOWN' })).toBeVisible()
})
it('shows each server engine from metadata after its name, including when the first object has no engine', () => {
  render(
    <MemoryRouter>
      <CatalogSidebar
        nodes={[
          makeNode({ id: 'old', server: 'ATLAS_SQL' }),
          makeNode({ id: 'sql', server: 'ATLAS_SQL', engine: 'mssql' }),
          makeNode({ id: 'oracle', server: 'HELIOS_ORACLE', engine: 'oracle' }),
          makeNode({ id: 'unknown', server: 'LEGACY' }),
        ]}
      />
    </MemoryRouter>,
  )
  expect(screen.getByRole('button', { name: 'ATLAS_SQL (SQL Server) 2' })).toBeVisible()
  expect(screen.getByRole('button', { name: 'HELIOS_ORACLE (Oracle) 1' })).toBeVisible()
  expect(screen.getByRole('button', { name: 'LEGACY 1' })).toBeVisible()
})

it('sorts scoped object types before child containers, alphabetically within each group', () => {
  render(
    <MemoryRouter>
      <CatalogSidebar
        nodes={[
          makeNode({ id: 'zlogin', type: 'Logins', database: '_ServerLevel', schema: null }),
          makeNode({ id: 'alink', type: 'LinkedServers', database: '_ServerLevel', schema: null }),
          makeNode({ id: 'zdb', database: 'Zebra', schema: 'zeta' }),
          makeNode({ id: 'zschema', database: 'Alpha', schema: 'zeta' }),
          makeNode({ id: 'aschema', database: 'Alpha', schema: 'alpha' }),
          makeNode({ id: 'role', database: 'Alpha', schema: null, type: 'Roles' }),
          makeNode({ id: 'replication', database: 'Alpha', schema: null, type: 'Replication' }),
        ]}
      />
    </MemoryRouter>,
  )
  const server = screen.getByRole('button', { name: /SRV1/ })
  fireEvent.click(server)
  expect(
    within(server.parentElement!)
      .getAllByRole('button')
      .slice(1)
      .map((button) => button.textContent?.replace(/^[▾▸]|\d+$/g, '')),
  ).toEqual(['LinkedServers', 'Logins', 'Alpha', 'Zebra'])
  const database = screen.getByRole('button', { name: /Alpha/ })
  fireEvent.click(database)
  expect(
    within(database.parentElement!)
      .getAllByRole('button')
      .slice(1)
      .map((button) => button.textContent?.replace(/^[▾▸]|\d+$/g, '')),
  ).toEqual(['Replication', 'Roles', 'alpha', 'zeta'])
})
it('opens linked servers directly under the server without a _ServerLevel branch', () => {
  const link = makeNode({
    id: 'SRV1/_ServerLevel/LinkedServers/REMOTE',
    name: 'REMOTE',
    qualifiedName: 'REMOTE',
    database: '_ServerLevel',
    type: 'LinkedServers',
    schema: null,
  })
  render(
    <MemoryRouter>
      <CatalogSidebar nodes={[link]} />
    </MemoryRouter>,
  )
  expect(screen.getByRole('complementary', { name: 'Catalog' })).toBeVisible()
  expect(screen.queryByRole('button', { name: 'Catalog' })).not.toBeInTheDocument()
  expect(screen.queryByRole('button', { name: /Close/ })).not.toBeInTheDocument()
  fireEvent.click(screen.getByRole('button', { name: /SRV1/ }))
  expect(screen.queryByRole('button', { name: /_ServerLevel/ })).not.toBeInTheDocument()
  fireEvent.click(screen.getByRole('button', { name: /LinkedServers/ }))
  expect(screen.queryByRole('button', { name: /no schema/ })).not.toBeInTheDocument()
  expect(screen.getByRole('link', { name: /REMOTE/ })).toHaveAttribute('href', `/object/${link.id}`)
})

it('places all server-level types beside databases and keeps the original object routes', () => {
  const login = makeNode({
    id: 'SRV1/_ServerLevel/Logins/reader',
    name: 'reader',
    qualifiedName: 'reader',
    database: '_ServerLevel',
    type: 'Logins',
    schema: null,
  })
  const link = makeNode({
    id: 'SRV1/_ServerLevel/LinkedServers/REMOTE',
    name: 'REMOTE',
    qualifiedName: 'REMOTE',
    database: '_ServerLevel',
    type: 'LinkedServers',
    schema: null,
  })
  render(
    <MemoryRouter>
      <CatalogSidebar nodes={[login, link, makeNode({ id: 'table' })]} />
    </MemoryRouter>,
  )
  fireEvent.click(screen.getByRole('button', { name: /SRV1/ }))
  expect(screen.getByRole('button', { name: /AppDb/ })).toBeVisible()
  expect(screen.getByRole('button', { name: /LinkedServers/ })).toBeVisible()
  fireEvent.click(screen.getByRole('button', { name: /Logins/ }))
  expect(screen.getByRole('link', { name: /reader/ })).toHaveAttribute('href', `/object/${login.id}`)
  expect(screen.queryByRole('button', { name: /_ServerLevel/ })).not.toBeInTheDocument()
})
it('opens scoped explorer filters while expanding and exposes every object beyond the branch cap', () => {
  const nodes = Array.from({ length: 101 }, (_, i) =>
    makeNode({ id: `object${i}`, qualifiedName: `dbo.Object${String(i).padStart(3, '0')}` }),
  )
  render(
    <MemoryRouter initialEntries={['/explorer?q=Orders']}>
      <CatalogSidebar nodes={nodes} />
      <Location />
    </MemoryRouter>,
  )
  fireEvent.click(screen.getByRole('button', { name: /SRV1/ }))
  expect(screen.getByTestId('location')).toHaveTextContent('/server/SRV1')
  fireEvent.click(screen.getByRole('button', { name: /AppDb/ }))
  fireEvent.click(screen.getByRole('button', { name: /dbo/ }))
  fireEvent.click(screen.getByRole('button', { name: /Tables/ }))
  const location = screen.getByTestId('location').textContent!
  expect(location).toMatch(/^\/explorer\?filters=/)
  expect(
    decodeTokensFromUrl(new URLSearchParams(location.split('?')[1]).get('filters')).map(({ attribute, values }) => ({
      attribute,
      values,
    })),
  ).toEqual([
    { attribute: 'server', values: ['SRV1'] },
    { attribute: 'database', values: ['AppDb'] },
    { attribute: 'schema', values: ['dbo'] },
    { attribute: 'type', values: ['Tables'] },
  ])
  expect(screen.getAllByRole('link')).toHaveLength(100)
  fireEvent.click(screen.getByRole('button', { name: 'Show more (1)' }))
  expect(screen.getAllByRole('link')).toHaveLength(101)
  fireEvent.click(screen.getByRole('link', { name: /dbo.Object100/ }))
  expect(screen.getByTestId('location')).toHaveTextContent('/object/object100')
})

it('resizes with the keyboard, clamps the width, and remembers the preference', () => {
  window.localStorage.removeItem('catalog-sidebar-width')
  const { unmount } = render(
    <MemoryRouter>
      <CatalogSidebar nodes={[]} />
    </MemoryRouter>,
  )
  const handle = screen.getByRole('separator', { name: 'Resize catalog sidebar' })
  fireEvent.keyDown(handle, { key: 'ArrowRight' })
  expect(handle).toHaveAttribute('aria-valuenow', '300')
  fireEvent.keyDown(handle, { key: 'End' })
  fireEvent.keyDown(handle, { key: 'ArrowRight' })
  expect(handle).toHaveAttribute('aria-valuenow', '600')
  unmount()
  render(
    <MemoryRouter>
      <CatalogSidebar nodes={[]} />
    </MemoryRouter>,
  )
  expect(screen.getByRole('separator')).toHaveAttribute('aria-valuenow', '600')
  window.localStorage.removeItem('catalog-sidebar-width')
})

it('groups database-level objects by type without a schema placeholder', () => {
  render(
    <MemoryRouter>
      <CatalogSidebar
        nodes={[
          makeNode({ id: 'pub', name: 'Orders_Pub', qualifiedName: 'Orders_Pub', type: 'Replication', schema: null }),
        ]}
      />
    </MemoryRouter>,
  )
  fireEvent.click(screen.getByRole('button', { name: /SRV1/ }))
  fireEvent.click(screen.getByRole('button', { name: /AppDb/ }))
  fireEvent.click(screen.getByRole('button', { name: /Replication/ }))
  expect(screen.getByRole('link', { name: /Orders_Pub/ })).toHaveAttribute('href', '/object/pub')
  expect(screen.queryByText('(unknown)')).not.toBeInTheDocument()
})
