import { fireEvent, render, screen, within } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { expect, it, vi } from 'vitest'
import History from './History'
import { buildIndex } from '../lib/catalog'
import { makeCatalog, makeNode } from '../test/fixtures'
vi.mock('../lib/CatalogContext', () => ({
  useCatalog: () => ({
    index: buildIndex(
      makeCatalog({
        nodes: [makeNode({ id: 'known' })],
        recentChanges: [
          { sha: '123456789', date: '2026-02-01', message: 'First commit', objectIds: ['known', 'unextracted'] },
          { sha: '987654321', date: '2026-01-01', message: 'Second commit', objectIds: ['known'] },
        ],
      }),
    ),
  }),
}))
it('independently expands commits, retains full SHA and counts unavailable object IDs', () => {
  render(
    <MemoryRouter>
      <History />
    </MemoryRouter>,
  )
  const first = screen.getByRole('button', { name: /First commit/ })
  const second = screen.getByRole('button', { name: /Second commit/ })
  expect(first).toHaveTextContent('2 objects')
  fireEvent.click(first)
  fireEvent.click(second)
  expect(first).toHaveAttribute('aria-expanded', 'true')
  expect(second).toHaveAttribute('aria-expanded', 'true')
  expect(screen.getByText('123456789')).toBeInTheDocument()
  const list = document.getElementById(first.getAttribute('aria-controls')!)!
  expect(within(list).getAllByRole('link')).toHaveLength(1)
  expect(within(list).getByRole('link')).toHaveAttribute('href', '/object/known')
  fireEvent.click(first)
  expect(first).toHaveAttribute('aria-expanded', 'false')
  expect(second).toHaveAttribute('aria-expanded', 'true')
})
