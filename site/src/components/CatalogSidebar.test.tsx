import { fireEvent, render, screen } from '@testing-library/react'
import { MemoryRouter, useLocation } from 'react-router-dom'
import { expect, it } from 'vitest'
import CatalogSidebar from './CatalogSidebar'
import { makeNode } from '../test/fixtures'
function Location() { const location = useLocation(); return <output data-testid="location">{location.pathname}{location.search}</output> }
it('opens server-level linked servers directly without a schema branch', () => {
  const link = makeNode({ id: 'SRV1/_ServerLevel/LinkedServers/REMOTE', name: 'REMOTE', qualifiedName: 'REMOTE', database: '_ServerLevel', type: 'LinkedServers', schema: null })
  render(<MemoryRouter><CatalogSidebar nodes={[link]} /></MemoryRouter>)
  expect(screen.getByRole('complementary', { name: 'Catalog' })).toBeVisible()
  expect(screen.queryByRole('button', { name: 'Catalog' })).not.toBeInTheDocument()
  expect(screen.queryByRole('button', { name: /Close/ })).not.toBeInTheDocument()
  fireEvent.click(screen.getByRole('button', { name: /SRV1/ }))
  fireEvent.click(screen.getByRole('button', { name: /_ServerLevel/ }))
  expect(screen.queryByRole('button', { name: /no schema/ })).not.toBeInTheDocument()
  expect(screen.getByRole('link', { name: /REMOTE/ })).toHaveAttribute('href', `/object/${link.id}`)
})
it('expands context without changing filters and exposes every object beyond the branch cap', () => {
  const nodes = Array.from({ length: 101 }, (_, i) => makeNode({ id: `object${i}`, qualifiedName: `dbo.Object${String(i).padStart(3, '0')}` }))
  render(<MemoryRouter initialEntries={['/explorer?q=Orders']}><CatalogSidebar nodes={nodes} /><Location /></MemoryRouter>)
  fireEvent.click(screen.getByRole('button', { name: /SRV1/ }))
  fireEvent.click(screen.getByRole('button', { name: /AppDb/ }))
  fireEvent.click(screen.getByRole('button', { name: /dbo/ }))
  expect(screen.getByTestId('location')).toHaveTextContent('/explorer?q=Orders')
  expect(screen.getAllByRole('link')).toHaveLength(100)
  fireEvent.click(screen.getByRole('button', { name: 'Show more (1)' }))
  expect(screen.getAllByRole('link')).toHaveLength(101)
  fireEvent.click(screen.getByRole('link', { name: /dbo.Object100/ }))
  expect(screen.getByTestId('location')).toHaveTextContent('/object/object100')
})
