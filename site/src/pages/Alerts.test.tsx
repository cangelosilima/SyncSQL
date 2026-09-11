import { fireEvent, render, screen } from '@testing-library/react'
import { MemoryRouter, useLocation } from 'react-router-dom'
import { expect, it, vi } from 'vitest'
import Alerts from './Alerts'
import { buildIndex } from '../lib/catalog'
import { makeCatalog, makeNode } from '../test/fixtures'

const catalog = makeCatalog({
  nodes: [
    makeNode({
      id: 'orders',
      metrics: [1000, 2000].map((rowCount, i) => ({
        capturedAt: `2026-09-0${i + 1}`,
        rowCount,
        reservedKB: null,
        dataKB: null,
        indexKB: null,
        indexes: [],
        statistics: [],
      })),
    }),
  ],
  orphanedReferences: Array.from({ length: 105 }, (_, i) => ({
    from: 'orders',
    name: `Missing${i}`,
    schema: 'dbo',
    database: null,
    server: null,
  })),
})
vi.mock('../lib/CatalogContext', () => ({ useCatalog: () => ({ index: buildIndex(catalog) }) }))
function Location() {
  return <span data-testid="location">{useLocation().search}</span>
}
it('consolidates all findings, filters with shareable URL state, and preserves investigation links', () => {
  render(
    <MemoryRouter>
      <Alerts />
      <Location />
    </MemoryRouter>,
  )
  expect(screen.getByRole('status')).toHaveTextContent('106 of 106 alerts match · showing 100')
  fireEvent.click(screen.getByRole('button', { name: 'Show more (6)' }))
  expect(screen.getAllByRole('row')).toHaveLength(107)
  fireEvent.change(screen.getByLabelText('Category'), { target: { value: 'metrics' } })
  expect(screen.getByRole('status')).toHaveTextContent('1 of 106 alerts match')
  expect(screen.getByText('Heuristic')).toBeInTheDocument()
  expect(screen.getByRole('link', { name: 'Explore lineage' })).toHaveAttribute('href', '/lineage?focus=orders')
  expect(screen.getByTestId('location')).toHaveTextContent('category=metrics')
  fireEvent.change(screen.getByLabelText('Search alerts'), { target: { value: 'nonexistent' } })
  expect(screen.getByText('No alerts match these filters.')).toBeInTheDocument()
})
it('restores category and search from a deep link', () => {
  render(
    <MemoryRouter initialEntries={['/alerts?category=orphans&q=Missing104']}>
      <Alerts />
    </MemoryRouter>,
  )
  expect(screen.getByRole('status')).toHaveTextContent('1 of 106 alerts match')
  expect(screen.getByText('Unresolved reference to dbo.Missing104')).toBeInTheDocument()
})
