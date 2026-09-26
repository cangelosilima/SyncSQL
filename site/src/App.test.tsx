import { fireEvent, render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import App from './App'
import { buildIndex } from './lib/catalog'
import { makeCatalog } from './test/fixtures'

let catalogState = {
  loading: false,
  error: null as string | null,
  index: buildIndex(makeCatalog()) as ReturnType<typeof buildIndex> | null,
}

const state = vi.hoisted(() => ({
  ai: {
    checking: false,
    available: false,
    reason: 'model-missing' as string | null,
    runtimeStatus: 'idle',
    progress: null,
    error: null,
    generateFilterPlan: vi.fn(),
    retry: vi.fn(),
  },
}))

vi.mock('./lib/CatalogContext', () => ({
  CatalogProvider: ({ children }: { children: React.ReactNode }) => children,
  useCatalog: () => catalogState,
}))

vi.mock('./ai/AiContext', () => ({
  AiProvider: ({ children }: { children: React.ReactNode }) => children,
  useAi: () => state.ai,
}))

describe('AI navigation and direct routing', () => {
  beforeEach(() => {
    catalogState = { loading: false, error: null, index: buildIndex(makeCatalog()) }
    state.ai.checking = false
    state.ai.available = false
    state.ai.reason = 'model-missing'
  })

  it('renders an empty explorer while the catalog is absent', () => {
    catalogState.index = null
    render(
      <MemoryRouter initialEntries={['/explorer']}>
        <App />
      </MemoryRouter>,
    )
    expect(screen.getByRole('main')).toBeInTheDocument()
  })

  it('shows loading and catalog failure states', () => {
    catalogState.loading = true
    const { rerender } = render(
      <MemoryRouter>
        <App />
      </MemoryRouter>,
    )
    expect(screen.getByRole('status')).toHaveTextContent('Loading catalog')
    catalogState.loading = false
    catalogState.error = 'Offline'
    rerender(
      <MemoryRouter>
        <App />
      </MemoryRouter>,
    )
    expect(screen.getByRole('alert')).toHaveTextContent('Failed to load catalog: Offline')
  })

  it.each(['/explorer', '/object/missing', '/server/SRV1'])(
    'renders contextual navigation on %s and focuses the skip target',
    (path) => {
      const scrollTo = vi.fn()
      Object.defineProperty(HTMLElement.prototype, 'scrollTo', { value: scrollTo, configurable: true })
      catalogState.index!.catalog.example = {
        title: 'Demo',
        description: 'Example',
      } as typeof catalogState.index.catalog.example
      render(
        <MemoryRouter initialEntries={[path]}>
          <App />
        </MemoryRouter>,
      )
      expect(screen.getByText('Example catalog')).toBeVisible()
      fireEvent.click(screen.getByRole('link', { name: 'Skip to content' }))
      expect(document.getElementById('main-content')).toHaveFocus()
      expect(scrollTo).toHaveBeenCalledWith({ top: 0 })
      Reflect.deleteProperty(HTMLElement.prototype, 'scrollTo')
    },
  )

  it.each([
    [true, null, 'Checking AI availability'],
    [false, 'runtime-error', 'AI disabled after the local model failed to load'],
  ] as const)('explains disabled AI navigation', (checking, reason, title) => {
    state.ai.checking = checking
    state.ai.reason = reason
    catalogState.index = null
    render(
      <MemoryRouter initialEntries={['/ai']}>
        <App />
      </MemoryRouter>,
    )
    expect(screen.getByText('AI', { selector: 'span.nav-link-disabled' })).toHaveAttribute('title', title)
  })

  it('renders a visible, non-clickable AI navigation item when the model is unavailable', () => {
    render(
      <MemoryRouter initialEntries={['/ai']}>
        <App />
      </MemoryRouter>,
    )
    const item = screen.getByText('AI', { selector: 'span.nav-link-disabled' })
    expect(item).toHaveAttribute('aria-disabled', 'true')
    expect(item).toHaveAttribute('title', 'AI model not included in this deployment')
    expect(item.closest('a')).toBeNull()
  })

  it('shows the unavailable page for a direct AI route', () => {
    render(
      <MemoryRouter initialEntries={['/ai']}>
        <App />
      </MemoryRouter>,
    )
    expect(screen.getByRole('heading', { name: 'AI filter generation is unavailable' })).toBeInTheDocument()
    expect(screen.getByText('The local model was not included in this site deployment.')).toBeInTheDocument()
  })

  it('enables the AI navigation link when the capability is available', () => {
    state.ai.available = true
    state.ai.reason = null
    render(
      <MemoryRouter initialEntries={['/ai']}>
        <App />
      </MemoryRouter>,
    )
    expect(screen.getByRole('link', { name: 'AI' })).toHaveAttribute('href', '/ai')
  })
})
