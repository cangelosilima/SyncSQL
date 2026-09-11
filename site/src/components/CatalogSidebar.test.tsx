import { fireEvent, render, screen, within } from '@testing-library/react'
import { MemoryRouter, useLocation } from 'react-router-dom'
import { expect, it } from 'vitest'
import CatalogSidebar from './CatalogSidebar'
import { makeNode } from '../test/fixtures'
function Location() { const location = useLocation(); return <output data-testid="location">{location.pathname}{location.search}</output> }
it('labels link destinations using target metadata, including aliases, without using the host engine', () => {
  const links = ['REMOTE', 'ALIAS', 'UNKNOWN'].map(name => makeNode({ id: name, name, qualifiedName: name, server: 'HOST', engine: 'mssql', database: '_ServerLevel', schema: null, type: 'LinkedServers' }))
  render(<MemoryRouter><CatalogSidebar nodes={[...links, makeNode({ id: 'target', server: 'remote', engine: 'oracle' })]} linkedServerReferences={[{ linkedServer: 'ALIAS', from: 'caller', to: 'target', schema: null, name: 'target' }]} /></MemoryRouter>)
  fireEvent.click(screen.getByRole('button', { name: /HOST/ }))
  fireEvent.click(screen.getByRole('button', { name: /LinkedServers/ }))
  expect(screen.getByRole('link', { name: 'REMOTE (Oracle)' })).toHaveAttribute('href', '/object/REMOTE')
  expect(screen.getByRole('link', { name: 'ALIAS (Oracle)' })).toBeVisible()
  expect(screen.getByRole('link', { name: 'UNKNOWN' })).toBeVisible()
})
it('shows each server engine from metadata after its name, including when the first object has no engine', () => {
  render(<MemoryRouter><CatalogSidebar nodes={[
    makeNode({ id: 'old', server: 'ATLAS_SQL' }),
    makeNode({ id: 'sql', server: 'ATLAS_SQL', engine: 'mssql' }),
    makeNode({ id: 'oracle', server: 'HELIOS_ORACLE', engine: 'oracle' }),
    makeNode({ id: 'unknown', server: 'LEGACY' }),
  ]} /></MemoryRouter>)
  expect(screen.getByRole('button', { name: 'ATLAS_SQL (SQL Server) 2' })).toBeVisible()
  expect(screen.getByRole('button', { name: 'HELIOS_ORACLE (Oracle) 1' })).toBeVisible()
  expect(screen.getByRole('button', { name: 'LEGACY 1' })).toBeVisible()
})

it('sorts scoped object types before child containers, alphabetically within each group', () => {
  render(<MemoryRouter><CatalogSidebar nodes={[
    makeNode({ id: 'zlogin', type: 'Logins', database: '_ServerLevel', schema: null }),
    makeNode({ id: 'alink', type: 'LinkedServers', database: '_ServerLevel', schema: null }),
    makeNode({ id: 'zdb', database: 'Zebra', schema: 'zeta' }),
    makeNode({ id: 'zschema', database: 'Alpha', schema: 'zeta' }),
    makeNode({ id: 'aschema', database: 'Alpha', schema: 'alpha' }),
    makeNode({ id: 'role', database: 'Alpha', schema: null, type: 'Roles' }),
    makeNode({ id: 'replication', database: 'Alpha', schema: null, type: 'Replication' }),
  ]} /></MemoryRouter>)
  const server = screen.getByRole('button', { name: /SRV1/ })
  fireEvent.click(server)
  expect(within(server.parentElement!).getAllByRole('button').slice(1).map(button => button.textContent?.replace(/^[▾▸]|\d+$/g, ''))).toEqual(['LinkedServers', 'Logins', 'Alpha', 'Zebra'])
  const database = screen.getByRole('button', { name: /Alpha/ })
  fireEvent.click(database)
  expect(within(database.parentElement!).getAllByRole('button').slice(1).map(button => button.textContent?.replace(/^[▾▸]|\d+$/g, ''))).toEqual(['Replication', 'Roles', 'alpha', 'zeta'])
})
it('opens linked servers directly under the server without a _ServerLevel branch', () => {
  const link = makeNode({ id: 'SRV1/_ServerLevel/LinkedServers/REMOTE', name: 'REMOTE', qualifiedName: 'REMOTE', database: '_ServerLevel', type: 'LinkedServers', schema: null })
  render(<MemoryRouter><CatalogSidebar nodes={[link]} /></MemoryRouter>)
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
  const login = makeNode({ id: 'SRV1/_ServerLevel/Logins/reader', name: 'reader', qualifiedName: 'reader', database: '_ServerLevel', type: 'Logins', schema: null })
  const link = makeNode({ id: 'SRV1/_ServerLevel/LinkedServers/REMOTE', name: 'REMOTE', qualifiedName: 'REMOTE', database: '_ServerLevel', type: 'LinkedServers', schema: null })
  render(<MemoryRouter><CatalogSidebar nodes={[login, link, makeNode({ id: 'table' })]} /></MemoryRouter>)
  fireEvent.click(screen.getByRole('button', { name: /SRV1/ }))
  expect(screen.getByRole('button', { name: /AppDb/ })).toBeVisible()
  expect(screen.getByRole('button', { name: /LinkedServers/ })).toBeVisible()
  fireEvent.click(screen.getByRole('button', { name: /Logins/ }))
  expect(screen.getByRole('link', { name: /reader/ })).toHaveAttribute('href', `/object/${login.id}`)
  expect(screen.queryByRole('button', { name: /_ServerLevel/ })).not.toBeInTheDocument()
})
it('expands context without changing filters and exposes every object beyond the branch cap', () => {
  const nodes = Array.from({ length: 101 }, (_, i) => makeNode({ id: `object${i}`, qualifiedName: `dbo.Object${String(i).padStart(3, '0')}` }))
  render(<MemoryRouter initialEntries={['/explorer?q=Orders']}><CatalogSidebar nodes={nodes} /><Location /></MemoryRouter>)
  fireEvent.click(screen.getByRole('button', { name: /SRV1/ }))
  fireEvent.click(screen.getByRole('button', { name: /AppDb/ }))
  fireEvent.click(screen.getByRole('button', { name: /dbo/ }))
  fireEvent.click(screen.getByRole('button', { name: /Tables/ }))
  expect(screen.getByTestId('location')).toHaveTextContent('/explorer?q=Orders')
  expect(screen.getAllByRole('link')).toHaveLength(100)
  fireEvent.click(screen.getByRole('button', { name: 'Show more (1)' }))
  expect(screen.getAllByRole('link')).toHaveLength(101)
  fireEvent.click(screen.getByRole('link', { name: /dbo.Object100/ }))
  expect(screen.getByTestId('location')).toHaveTextContent('/object/object100')
})

it('groups database-level objects by type without a schema placeholder', () => {
  render(<MemoryRouter><CatalogSidebar nodes={[makeNode({ id: 'pub', name: 'Orders_Pub', qualifiedName: 'Orders_Pub', type: 'Replication', schema: null })]} /></MemoryRouter>)
  fireEvent.click(screen.getByRole('button', { name: /SRV1/ }))
  fireEvent.click(screen.getByRole('button', { name: /AppDb/ }))
  fireEvent.click(screen.getByRole('button', { name: /Replication/ }))
  expect(screen.getByRole('link', { name: /Orders_Pub/ })).toHaveAttribute('href', '/object/pub')
  expect(screen.queryByText('(unknown)')).not.toBeInTheDocument()
})
