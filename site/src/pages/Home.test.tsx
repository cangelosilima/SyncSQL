import { render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { expect, it, vi } from 'vitest'
import Home from './Home'
import { buildIndex } from '../lib/catalog'
import { makeCatalog, makeNode } from '../test/fixtures'

const catalog = makeCatalog({
  nodes: Array.from({ length: 12 }, (_, i) =>
    makeNode({
      id: `orders${i}`,
      metrics: [1000, 2000].map((rowCount, j) => ({
        capturedAt: `2026-09-0${j + 1}`,
        rowCount,
        reservedKB: null,
        dataKB: null,
        indexKB: null,
        indexes: [],
        statistics: [],
      })),
    }),
  ),
  orphanedReferences: [{ from: 'orders0', name: 'Missing', schema: 'dbo', database: null, server: null }],
})
vi.mock('../lib/CatalogContext', () => ({ useCatalog: () => ({ index: buildIndex(catalog) }) }))

it('summarizes all alerts between lineage and last change and links to investigation', () => {
  const { container } = render(
    <MemoryRouter>
      <Home />
    </MemoryRouter>,
  )
  expect([...container.querySelectorAll('.quick-stat-label')].map((el) => el.textContent)).toEqual([
    'Total objects',
    'Commits mined',
    'Lineage edges',
    'Alerts',
    'Last change',
  ])
  expect(screen.getByRole('link', { name: 'Alerts' })).toHaveAttribute('href', '/alerts')
  expect(screen.getByText('13')).toBeVisible()
  expect(screen.getByText('12 anomalies · 1 orphaned references')).toBeVisible()
  expect(screen.queryByRole('heading', { name: /Metric anomalies|Orphaned references/ })).not.toBeInTheDocument()
})
