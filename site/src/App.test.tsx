import { render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import App from './App'

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
  useCatalog: () => ({
    loading: false,
    error: null,
    index: { catalog: { servers: [], nodes: [] } },
  }),
}))

vi.mock('./ai/AiContext', () => ({
  AiProvider: ({ children }: { children: React.ReactNode }) => children,
  useAi: () => state.ai,
}))

vi.mock('./lib/ThemeContext', () => ({
  useTheme: () => ({ theme: 'light', toggleTheme: vi.fn() }),
}))

describe('AI navigation and direct routing', () => {
  beforeEach(() => {
    state.ai.checking = false
    state.ai.available = false
    state.ai.reason = 'model-missing'
  })

  it('renders a visible, non-clickable AI navigation item when the model is unavailable', () => {
    render(<MemoryRouter initialEntries={['/ai']}><App /></MemoryRouter>)
    const item = screen.getByText('AI', { selector: 'span.nav-link-disabled' })
    expect(item).toHaveAttribute('aria-disabled', 'true')
    expect(item).toHaveAttribute('title', 'AI model not included in this deployment')
    expect(item.closest('a')).toBeNull()
  })

  it('shows the unavailable page for a direct AI route', () => {
    render(<MemoryRouter initialEntries={['/ai']}><App /></MemoryRouter>)
    expect(screen.getByRole('heading', { name: 'AI filter generation is unavailable' })).toBeInTheDocument()
    expect(screen.getByText('The local model was not included in this site deployment.')).toBeInTheDocument()
  })

  it('enables the AI navigation link when the capability is available', () => {
    state.ai.available = true
    state.ai.reason = null
    render(<MemoryRouter initialEntries={['/ai']}><App /></MemoryRouter>)
    expect(screen.getByRole('link', { name: 'AI' })).toHaveAttribute('href', '/ai')
  })
})
