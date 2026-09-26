import { fireEvent, render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { beforeEach, expect, it, vi } from 'vitest'
import LineagePage from './LineagePage'
import { buildIndex, type CatalogIndex } from '../lib/catalog'
import { makeCatalog, makeEdge, makeNode } from '../test/fixtures'
import { applyFilters, encodeTokensForUrl, type FilterToken } from '../lib/filters'
let base: CatalogIndex | null
let selected: CatalogIndex | null
vi.mock('../lib/CatalogContext', () => ({ useCatalog: () => ({ index: base }) }))
vi.mock('../lib/useCatalogData', () => ({
  useCatalogSelection: () => ({ index: selected }),
  useCatalogFilter: (_: unknown, tokens: FilterToken[]) => ({ nodes: applyFilters(base?.catalog.nodes ?? [], tokens) }),
}))
vi.mock('../components/LineageGraph', () => ({
  default: ({ nodeIds, onNodeActivate }: { nodeIds: string[]; onNodeActivate: (id: string) => void }) => (
    <div>
      {nodeIds.map((id) => (
        <button key={id} onClick={() => onNodeActivate(id)}>
          Focus {id}
        </button>
      ))}
    </div>
  ),
}))
beforeEach(() => {
  base = selected = buildIndex(makeCatalog())
})
const view = (url: string) => (
  <MemoryRouter initialEntries={[url]}>
    <LineagePage />
  </MemoryRouter>
)
it.each(['/lineage', '/lineage?focus=missing'])('waits for a catalog on %s', (url) => {
  base = selected = null
  expect(render(view(url)).container).toBeEmptyDOMElement()
})
it('uses permission summaries while selected details are absent', () => {
  base = buildIndex(
    makeCatalog({
      nodes: [
        makeNode({
          id: 'summary',
          qualifiedName: 'dbo.summary',
          grants: [{ grantee: 'reader', permission: 'SELECT', state: 'GRANT', granteeType: null, column: null }],
        }),
      ],
    }),
  )
  render(view('/lineage?grantee=reader'))
  expect(screen.getByRole('link', { name: 'dbo.summary' })).toBeVisible()
})
it('preserves unavailable breadcrumb identities and can clear an unknown focus', () => {
  base = selected = buildIndex(makeCatalog({ nodes: [makeNode({ id: 'next' })], edges: [makeEdge('missing', 'next')] }))
  const page = render(view('/lineage?focus=missing'))
  expect(screen.getByText(/2 object\(s\) around missing/)).toBeVisible()
  fireEvent.click(screen.getByRole('button', { name: 'Focus next' }))
  expect(screen.getByRole('button', { name: 'missing' })).toBeVisible()
  fireEvent.click(screen.getByRole('button', { name: 'missing' }))
  fireEvent.click(screen.getByRole('button', { name: 'Clear focus' }))
  expect(screen.queryByText('Navigating:')).not.toBeInTheDocument()
  page.unmount()
  render(view('/lineage?focus=missing&q=absent'))
  expect(screen.getByText(/Showing 1 of 2 objects around missing/)).toBeVisible()
})
it('does not duplicate an existing name filter when clearing focus', () => {
  base = selected = buildIndex(makeCatalog({ nodes: [makeNode({ id: 'root', qualifiedName: 'dbo.root' })] }))
  const filter = encodeTokensForUrl([{ id: 'name', attribute: 'name', operator: 'is', values: ['dbo.root'] }])
  render(view(`/lineage?focus=root&filter=${encodeURIComponent(filter)}`))
  fireEvent.click(screen.getByRole('button', { name: 'Clear focus' }))
  expect(screen.getAllByRole('button', { name: /Remove filter/ })).toHaveLength(1)
})
it('retains multiple connectors while searching for an outer object', () => {
  base = selected = buildIndex(
    makeCatalog({
      nodes: ['root', 'a', 'b', 'end'].map((id) => makeNode({ id, qualifiedName: 'dbo.' + id })),
      edges: [makeEdge('root', 'a'), makeEdge('a', 'b'), makeEdge('b', 'end')],
    }),
  )
  const filter = encodeTokensForUrl([{ id: 'name', attribute: 'name', operator: 'is', values: ['dbo.end'] }])
  render(view(`/lineage?focus=root&hops=3&filter=${encodeURIComponent(filter)}`))
  expect(screen.getByText(/2 connecting objects kept/)).toBeVisible()
})
it('explains absent focus grants and refines investigations from a recorded principal', () => {
  const grant = { grantee: 'reader', permission: 'SELECT', state: 'GRANT', granteeType: null, column: null }
  base = selected = buildIndex(
    makeCatalog({
      nodes: [makeNode({ id: 'root', qualifiedName: 'dbo.root' }), makeNode({ id: 'granted', grants: [grant] })],
      edges: [makeEdge('root', 'granted')],
    }),
  )
  render(view('/lineage?focus=root&grantee=read'))
  expect(screen.getByText('No grants recorded.')).toBeVisible()
  fireEvent.click(screen.getByRole('button', { name: 'Focus granted' }))
  fireEvent.click(screen.getByRole('button', { name: 'reader' }))
  expect(screen.queryByText('Navigating:')).not.toBeInTheDocument()
  expect(screen.getByRole('button', { name: /Remove filter/ })).toHaveAccessibleName(/reader/)
})
