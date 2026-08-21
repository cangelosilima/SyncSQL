import { createContext, useContext, useEffect, useState, type ReactNode } from 'react'

export type Theme = 'light' | 'dark'

const STORAGE_KEY = 'syncsql-theme'

interface ThemeContextValue {
  theme: Theme
  toggleTheme: () => void
}

const ThemeContext = createContext<ThemeContextValue>({ theme: 'light', toggleTheme: () => {} })

function readStoredTheme(): Theme {
  try {
    const stored = localStorage.getItem(STORAGE_KEY)
    return stored === 'dark' ? 'dark' : 'light'
  } catch {
    return 'light'
  }
}

/** Site-wide light/dark toggle. Light is the default - see styles.css for the rationale. */
export function ThemeProvider({ children }: { children: ReactNode }) {
  const [theme, setTheme] = useState<Theme>(readStoredTheme)

  useEffect(() => {
    document.documentElement.setAttribute('data-theme', theme)
    try {
      localStorage.setItem(STORAGE_KEY, theme)
    } catch {
      // Private browsing / storage disabled - theme just won't persist across reloads.
    }
  }, [theme])

  function toggleTheme() {
    setTheme((t) => (t === 'light' ? 'dark' : 'light'))
  }

  return <ThemeContext.Provider value={{ theme, toggleTheme }}>{children}</ThemeContext.Provider>
}

export function useTheme(): ThemeContextValue {
  return useContext(ThemeContext)
}
