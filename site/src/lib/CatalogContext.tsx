import { createContext, useContext, useEffect, useState, type ReactNode } from 'react'
import { loadCatalog, type CatalogIndex } from './catalog'

interface CatalogContextValue {
  index: CatalogIndex | null
  loading: boolean
  error: string | null
}

const CatalogContext = createContext<CatalogContextValue>({ index: null, loading: true, error: null })

export function CatalogProvider({ children }: { children: ReactNode }) {
  const [index, setIndex] = useState<CatalogIndex | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    let cancelled = false
    loadCatalog()
      .then((result) => {
        if (!cancelled) setIndex(result)
      })
      .catch((err: unknown) => {
        if (!cancelled) setError(err instanceof Error ? err.message : String(err))
      })
      .finally(() => {
        if (!cancelled) setLoading(false)
      })
    return () => {
      cancelled = true
    }
  }, [])

  return <CatalogContext.Provider value={{ index, loading, error }}>{children}</CatalogContext.Provider>
}

export function useCatalog(): CatalogContextValue {
  return useContext(CatalogContext)
}
