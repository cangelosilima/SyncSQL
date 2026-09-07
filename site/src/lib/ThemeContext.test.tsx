import { fireEvent, render, screen, cleanup } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { ThemeProvider, useTheme } from './ThemeContext'

function ThemeControl() {
  const { theme, toggleTheme } = useTheme()
  return <button onClick={toggleTheme}>{theme}</button>
}

describe('theme preference contract', () => {
  beforeEach(() => {
    // Node's optional Web Storage can shadow jsdom's storage. Keep this test
    // focused on the provider's browser-storage contract, not a host file.
    const values = new Map<string, string>()
    vi.stubGlobal('localStorage', {
      getItem: vi.fn((key: string) => values.get(key) ?? null),
      setItem: vi.fn((key: string, value: string) => { values.set(key, value) }),
    })
  })
  afterEach(() => {
    cleanup()
    vi.restoreAllMocks()
    vi.unstubAllGlobals()
    document.documentElement.removeAttribute('data-theme')
  })

  it.each([null, 'light', 'invalid'])('defaults to light for preference %s', (stored) => {
    if (stored !== null) localStorage.setItem('syncsql-theme', stored)
    render(<ThemeProvider><ThemeControl /></ThemeProvider>)
    expect(document.documentElement).toHaveAttribute('data-theme', 'light')
    expect(localStorage.getItem('syncsql-theme')).toBe('light')
  })

  it('respects saved dark mode and persists an explicit toggle across mounts', () => {
    localStorage.setItem('syncsql-theme', 'dark')
    const view = render(<ThemeProvider><ThemeControl /></ThemeProvider>)
    expect(document.documentElement).toHaveAttribute('data-theme', 'dark')
    fireEvent.click(screen.getByRole('button', { name: 'dark' }))
    expect(document.documentElement).toHaveAttribute('data-theme', 'light')
    expect(localStorage.getItem('syncsql-theme')).toBe('light')
    view.unmount()
    render(<ThemeProvider><ThemeControl /></ThemeProvider>)
    expect(screen.getByRole('button', { name: 'light' })).toBeInTheDocument()
  })

  it('keeps theme switching usable when storage is blocked', () => {
    vi.mocked(localStorage.getItem).mockImplementation(() => { throw new Error('Blocked') })
    vi.mocked(localStorage.setItem).mockImplementation(() => { throw new Error('Blocked') })
    render(<ThemeProvider><ThemeControl /></ThemeProvider>)
    fireEvent.click(screen.getByRole('button', { name: 'light' }))
    expect(document.documentElement).toHaveAttribute('data-theme', 'dark')
  })
})
