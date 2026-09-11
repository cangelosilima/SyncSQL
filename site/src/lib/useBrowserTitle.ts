import { useLayoutEffect } from 'react'
import { useLocation, useMatch } from 'react-router-dom'
import type { CatalogIndex } from './catalog'

const pageNames: Record<string, string> = {
  '/': 'Overview',
  '/explorer': 'Explorer',
  '/lineage': 'Lineage',
  '/alerts': 'Alerts',
  '/ai': 'AI',
  '/history': 'History',
}

/** Name the current tab/history entry without adding or replacing navigation. */
export function useBrowserTitle(index: Pick<CatalogIndex, 'byId'> | null) {
  const { pathname, search } = useLocation()
  // Match the object page itself so encoded IDs are decoded exactly as useParams.
  const objectRoute = useMatch('/object/*')
  const page = pathname.replace(/\/$/, '').toLowerCase() || '/'
  const params = new URLSearchParams(search)
  const id = objectRoute?.params['*'] ?? (page === '/lineage' ? params.get('focus') : null)
  const node = id ? index?.byId.get(id) : undefined
  let label = pageNames[page] ?? 'Page not found'

  if (objectRoute) label = index ? 'Object not found' : 'Object'
  if (node) {
    const column = objectRoute ? params.get('column') : null
    const name = [node.schema, node.name, column].filter(Boolean).join('.')
    const scope = [node.server, node.database === '_ServerLevel' ? null : node.database].filter(Boolean).join('/')
    label = `${page === '/lineage' ? 'Lineage · ' : ''}${name} · ${scope}`
  }

  const title = `${label} | SQLineage`
  // Apply before paint, including on Back/Forward and after catalog loading.
  useLayoutEffect(() => {
    document.title = title
  }, [title])
}
