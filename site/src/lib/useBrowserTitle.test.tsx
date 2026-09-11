import { fireEvent, render, screen } from '@testing-library/react'
import { MemoryRouter, useNavigate } from 'react-router-dom'
import { describe, expect, it } from 'vitest'
import type { CatalogNode } from '../types'
import { useBrowserTitle } from './useBrowserTitle'

const order = {
  id: 'SQLPROD01/SalesDb/Tables/dbo/Orders 100%',
  server: 'SQLPROD01',
  database: 'SalesDb',
  schema: 'dbo',
  name: 'Orders 100%',
} as CatalogNode
const remoteOrder = { ...order, id: 'SQLPROD02/SalesDb/Tables/dbo/Orders 100%', server: 'SQLPROD02' }
const link = {
  id: 'SQLPROD01/_ServerLevel/LinkedServers/REMOTE',
  server: 'SQLPROD01',
  database: '_ServerLevel',
  schema: null,
  name: 'REMOTE',
} as CatalogNode
const index = { byId: new Map([order, remoteOrder, link].map((node) => [node.id, node])) }
const objectUrl = (node: CatalogNode) => `/object/${encodeURIComponent(node.id)}`

function Page({ loaded = true }: { loaded?: boolean }) {
  useBrowserTitle(loaded ? index : null)
  const navigate = useNavigate()
  return (
    <>
      <button onClick={() => navigate(objectUrl(order))}>Object</button>
      <button onClick={() => navigate(objectUrl(remoteOrder))}>Remote object</button>
      <button onClick={() => navigate(`/lineage?focus=${encodeURIComponent(order.id)}`)}>Lineage</button>
      <button onClick={() => navigate('?column=CustomerId', { replace: true })}>Column</button>
      <button onClick={() => navigate(-1)}>Back</button>
      <button onClick={() => navigate(1)}>Forward</button>
    </>
  )
}

describe('browser tab and history titles', () => {
  it.each([
    ['/', 'Overview'],
    ['/explorer', 'Explorer'],
    ['/lineage', 'Lineage'],
    ['/alerts', 'Alerts'],
    ['/ai', 'AI'],
    ['/history', 'History'],
    ['/Explorer/', 'Explorer'],
    ['/missing', 'Page not found'],
    ['/object/missing', 'Object not found'],
  ])('names the direct route %s', (url, title) => {
    render(
      <MemoryRouter initialEntries={[url]}>
        <Page />
      </MemoryRouter>,
    )
    expect(document.title).toBe(`${title} | SQLineage`)
  })

  it('distinguishes objects across servers and restores titles on Back and Forward', () => {
    render(
      <MemoryRouter initialEntries={['/explorer']}>
        <Page />
      </MemoryRouter>,
    )
    fireEvent.click(screen.getByText('Object'))
    expect(document.title).toBe('dbo.Orders 100% · SQLPROD01/SalesDb | SQLineage')
    fireEvent.click(screen.getByText('Remote object'))
    expect(document.title).toBe('dbo.Orders 100% · SQLPROD02/SalesDb | SQLineage')
    fireEvent.click(screen.getByText('Back'))
    expect(document.title).toBe('dbo.Orders 100% · SQLPROD01/SalesDb | SQLineage')
    fireEvent.click(screen.getByText('Back'))
    expect(document.title).toBe('Explorer | SQLineage')
    fireEvent.click(screen.getByText('Forward'))
    expect(document.title).toBe('dbo.Orders 100% · SQLPROD01/SalesDb | SQLineage')
    fireEvent.click(screen.getByText('Lineage'))
    expect(document.title).toBe('Lineage · dbo.Orders 100% · SQLPROD01/SalesDb | SQLineage')
  })

  it('updates the selected column without adding another Back step', () => {
    render(
      <MemoryRouter initialEntries={['/explorer']}>
        <Page />
      </MemoryRouter>,
    )
    fireEvent.click(screen.getByText('Object'))
    fireEvent.click(screen.getByText('Column'))
    expect(document.title).toBe('dbo.Orders 100%.CustomerId · SQLPROD01/SalesDb | SQLineage')
    fireEvent.click(screen.getByText('Back'))
    expect(document.title).toBe('Explorer | SQLineage')
  })

  it('resolves an object title after the catalog loads', () => {
    const view = (loaded: boolean) => (
      <MemoryRouter initialEntries={[objectUrl(order)]}>
        <Page loaded={loaded} />
      </MemoryRouter>
    )
    const { rerender } = render(view(false))
    expect(document.title).toBe('Object | SQLineage')
    rerender(view(true))
    expect(document.title).toBe('dbo.Orders 100% · SQLPROD01/SalesDb | SQLineage')
  })

  it('names server-level objects without exposing the database placeholder', () => {
    render(
      <MemoryRouter initialEntries={[objectUrl(link)]}>
        <Page />
      </MemoryRouter>,
    )
    expect(document.title).toBe('REMOTE · SQLPROD01 | SQLineage')
  })
})
