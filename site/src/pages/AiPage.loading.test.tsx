import { fireEvent, render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { expect, it, vi } from 'vitest'
import AiPage from './AiPage'
const state = vi.hoisted(() => ({
  loading: false,
  error: undefined as string | undefined,
  aiError: null as string | null,
  column: false,
}))
vi.mock('../lib/CatalogContext', () => ({ useCatalog: () => ({ index: null }) }))
vi.mock('../lib/useCatalogData', () => ({
  useCatalogSelection: () => ({ index: null }),
  useCatalogFilter: () => ({ nodes: [], loading: state.loading, error: state.error }),
}))
vi.mock('../ai/AiContext', () => ({
  useAi: () => ({
    available: true,
    error: state.aiError,
    generateFilterPlan: async () => ({
      version: 1,
      tokens: [],
      contentQuery: '',
      confidence: 'medium',
      warnings: [],
      unsupportedFragments: [],
      ...(state.column ? { columnReference: { objectId: 'missing', column: 'Id' } } : {}),
    }),
  }),
}))
it('reports loading and failed preview searches and shared runtime failures', async () => {
  const view = render(
    <MemoryRouter>
      <AiPage />
    </MemoryRouter>,
  )
  fireEvent.change(screen.getByRole('textbox'), { target: { value: 'Find orders' } })
  fireEvent.click(screen.getByRole('button', { name: 'Generate filters' }))
  await screen.findByText('medium confidence')
  state.loading = true
  view.rerender(
    <MemoryRouter>
      <AiPage />
    </MemoryRouter>,
  )
  expect(screen.getByText('Loading matches...')).toBeVisible()
  state.loading = false
  state.error = 'Search failed'
  state.aiError = 'Runtime failed'
  view.rerender(
    <MemoryRouter>
      <AiPage />
    </MemoryRouter>,
  )
  expect(screen.getByText('Matches unavailable')).toBeVisible()
  expect(screen.getByText('Runtime failed')).toBeVisible()
  state.error = undefined
  state.column = true
  fireEvent.click(screen.getByRole('button', { name: 'Generate filters' }))
  expect(await screen.findByText('Column references')).toBeVisible()
})
