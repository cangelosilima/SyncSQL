import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import LineagePage from './LineagePage'
import { buildIndex } from '../lib/catalog'
import { makeCatalog, makeEdge, makeNode } from '../test/fixtures'
import snapshot from '../../public/data/catalog.json'
import type { Catalog } from '../types'
import { encodeTokensForUrl } from '../lib/filters'

/**
 * A table with three neighbours of two different types - the shape the reported
 * bug shows up on: drilling into the table, then asking for just the stored
 * procedures around it.
 */
const Order = makeNode({ id: 'Order', name: 'Order', qualifiedName: 'dbo.Order', type: 'Tables' })
const readProc = makeNode({ id: 'read', name: 'ReadOrder', qualifiedName: 'dbo.ReadOrder', type: 'StoredProcedures' })
const writeProc = makeNode({
  id: 'write',
  name: 'WriteOrder',
  qualifiedName: 'dbo.WriteOrder',
  type: 'StoredProcedures',
})
const summaryView = makeNode({ id: 'view', name: 'OrderSummary', qualifiedName: 'dbo.OrderSummary', type: 'Views' })

const baseCatalog = makeCatalog({
  nodes: [Order, readProc, writeProc, summaryView],
  edges: [makeEdge('read', 'Order'), makeEdge('write', 'Order'), makeEdge('view', 'Order')],
})
let catalog = baseCatalog

vi.mock('../lib/CatalogContext', () => ({
  useCatalog: () => ({ loading: false, error: null, index: buildIndex(catalog) }),
}))

// The graph is react-flow + dagre, which needs layout measurements jsdom does
// not provide. The assertions here are about which node ids the page selects,
// so the graph is stubbed down to a list of the names it was handed.
vi.mock('../components/LineageGraph', () => ({
  default: ({
    nodeIds,
    groupIntermediate,
    onNodeActivate,
  }: {
    nodeIds: string[]
    groupIntermediate?: boolean
    onNodeActivate: (id: string) => void
  }) => (
    <>
      <div data-testid="graph" data-grouped={String(groupIntermediate)}>
        {nodeIds.join(',')}
      </div>
      {nodeIds.map((id) => (
        <button key={id} onClick={() => onNodeActivate(id)}>
          Focus {id}
        </button>
      ))}
    </>
  ),
}))

function Location() {
  return <span data-testid="location">{useLocation().search}</span>
}

function renderAt(entry: string) {
  return render(
    <MemoryRouter initialEntries={[entry]}>
      <Routes>
        <Route path="/lineage" element={<LineagePage />} />
      </Routes>
      <Location />
    </MemoryRouter>,
  )
}

function graphIds(): string[] {
  const text = screen.getByTestId('graph').textContent ?? ''
  return text ? text.split(',') : []
}

/**
 * Drives the FilterBar's attribute -> operator -> value typeahead to commit one
 * token. The input's placeholder changes at every stage, so it is looked up once
 * by class and reused - React keeps the same DOM node across the re-renders.
 */
async function addTypeFilter(user: ReturnType<typeof userEvent.setup>, container: HTMLElement, value: string) {
  const input = container.querySelector<HTMLInputElement>('.filter-bar-input')
  if (!input) throw new Error('filter bar input not found')

  await user.type(input, 'Type')
  await user.click(await screen.findByRole('option', { name: /^Type$/ }))
  await user.click(await screen.findByRole('option', { name: /^is$/ }))
  await user.type(input, value)
  await user.click(await screen.findByRole('option', { name: value }))
}

describe('LineagePage', () => {
  beforeEach(() => {
    catalog = baseCatalog
  })
  it.each(['LinkedServers', 'DatabaseLinks'])('opens a %s search result with its callers and targets', async (type) => {
    catalog = snapshot as unknown as Catalog
    const user = userEvent.setup()
    const filter = encodeURIComponent(encodeTokensForUrl([{ attribute: 'type', operator: 'is', values: [type] }]))
    renderAt(`/lineage?filter=${filter}`)
    const link = catalog.nodes.find((node) => node.type === type)!
    expect(graphIds().every((id) => catalog.nodes.find((node) => node.id === id)!.type === type)).toBe(true)

    await user.click(screen.getByRole('button', { name: `Focus ${link.id}` }))
    const adjacent = catalog.edges
      .filter((edge) => edge.from === link.id || edge.to === link.id)
      .map((edge) => (edge.from === link.id ? edge.to : edge.from))
    expect(adjacent.length).toBeGreaterThan(0)
    expect(graphIds()).toEqual(expect.arrayContaining([link.id, ...adjacent]))
    expect(screen.queryByText(`Type is ${type}`)).not.toBeInTheDocument()
    expect(screen.getByTestId('location').textContent).not.toContain('filter=')
  })

  it('lets a restored filtered focus reveal its hidden neighborhood', async () => {
    catalog = snapshot as unknown as Catalog
    const user = userEvent.setup()
    const link = 'ATLAS_SQL/_ServerLevel/LinkedServers/HELIOS_ORACLE'
    const filter = encodeURIComponent(
      encodeTokensForUrl([{ attribute: 'type', operator: 'is', values: ['LinkedServers'] }]),
    )
    renderAt(`/lineage?focus=${encodeURIComponent(link)}&filter=${filter}&dependencies=3&dependents=2`)
    expect(graphIds()).toEqual([link])
    expect(screen.getByText('Type is LinkedServers')).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Show full neighborhood' }))
    expect(graphIds()).toContain('ATLAS_SQL/Commerce/StoredProcedures/ORDER_ENTRY/P_ONE')
    expect(graphIds()).toContain('HELIOS_ORACLE/FREEPDB1/Views/PROCUREMENT/V_ITEMS')
    expect(screen.getByRole('combobox', { name: 'Dependencies hops' })).toHaveValue('3')
    expect(screen.getByRole('combobox', { name: 'Dependents hops' })).toHaveValue('2')
    expect(screen.getByTestId('location').textContent).not.toContain('filter=')
  })

  it('keeps neighborhood filters when navigating between focused objects', async () => {
    const user = userEvent.setup()
    const { container } = renderAt('/lineage?focus=Order')
    await addTypeFilter(user, container, 'StoredProcedures')
    await user.click(screen.getByRole('button', { name: 'Focus read' }))
    expect(screen.getByText('Type is StoredProcedures')).toBeInTheDocument()
    expect(graphIds()).toEqual(['read'])
  })
  it('adds dependent hops along the focused benchmark object flow through shared links', async () => {
    catalog = snapshot as unknown as Catalog
    const user = userEvent.setup()
    const root = 'ATLAS_SQL/Commerce/Tables/ORDER_ENTRY/ITEMS'
    renderAt(`/lineage?focus=${encodeURIComponent(root)}&hops=0`)
    expect(graphIds()).toEqual([root])

    for (let hop = 1; hop <= 3; hop++) {
      await user.click(screen.getByRole('button', { name: 'Add dependents hop' }))
      const names = graphIds().map((id) => catalog.nodes.find((node) => node.id === id)!.qualifiedName)
      expect(names).toContain('PROCUREMENT.P_ONE')
      expect(names).toContain('PROCUREMENT.P_TWO')
      expect(names).not.toContain('ORDER_ENTRY.P_CIRCLE')
      expect(names).not.toContain('PROCUREMENT.P_CIRCLE')
      expect(names).not.toContain('PROCUREMENT.P_STOCK')
      expect(screen.getByRole('combobox', { name: 'Dependencies hops' })).toHaveValue('0')
      expect(screen.getByTestId('location')).toHaveTextContent(`dependents=${hop}`)
    }
    expect(graphIds()).toContain('ATLAS_SQL/Commerce/StoredProcedures/ORDER_ENTRY/P_READ')
    await user.click(screen.getByRole('checkbox', { name: 'Group intermediate layers' }))
    expect(screen.getByTestId('graph')).toHaveAttribute('data-grouped', 'true')
    expect(graphIds()).not.toContain('ATLAS_SQL/Commerce/StoredProcedures/ORDER_ENTRY/P_CIRCLE')
  })
  it('shows the focused object and its neighbours when arriving with ?focus=', () => {
    renderAt('/lineage?focus=Order')

    expect(graphIds().sort()).toEqual(['Order', 'read', 'view', 'write'])
  })

  it('does not seed a name filter token from ?focus=', () => {
    renderAt('/lineage?focus=Order')

    // The chip is what used to make every later filter contradictory.
    expect(screen.queryByText(/Name is dbo\.Order/i)).not.toBeInTheDocument()
  })

  /**
   * The reported bug: navigating into dbo.Order and adding "Type is
   * StoredProcedures" used to drop the focus and AND both tokens against the
   * whole catalog, which asked for an object that was both the table and a
   * stored procedure - so the graph emptied.
   */
  it('narrows the focused neighbourhood by type instead of emptying the graph', async () => {
    const user = userEvent.setup()
    const { container } = renderAt('/lineage?focus=Order')

    await addTypeFilter(user, container, 'StoredProcedures')

    const ids = graphIds().sort()
    expect(ids).toContain('Order')
    expect(ids).toContain('read')
    expect(ids).toContain('write')
    expect(ids).not.toContain('view')
  })

  it('keeps the focus when a filter is added', async () => {
    const user = userEvent.setup()
    const { container } = renderAt('/lineage?focus=Order')

    await addTypeFilter(user, container, 'StoredProcedures')

    expect(screen.getByText('Navigating:')).toBeInTheDocument()
    expect(screen.getByText(/Showing 3 of 4 objects around dbo\.Order/)).toBeInTheDocument()
  })

  it('turns the navigation into a name filter when focus is cleared', async () => {
    const user = userEvent.setup()
    renderAt('/lineage?focus=Order')

    await user.click(screen.getByRole('button', { name: /Clear focus/i }))

    // Not back to the whole catalog - still looking at that one object.
    expect(graphIds()).toEqual(['Order'])
    expect(screen.getByText(/Name is dbo\.Order/i)).toBeInTheDocument()
  })

  it('selects from the whole catalog when nothing is focused', () => {
    renderAt('/lineage')

    expect(graphIds().sort()).toEqual(['Order', 'read', 'view', 'write'])
  })
  it('adds hops on one side, persists grouping and restores the shared view', async () => {
    const user = userEvent.setup()
    catalog = makeCatalog({
      ...baseCatalog,
      nodes: [...baseCatalog.nodes, makeNode({ id: 'caller' }), makeNode({ id: 'data' })],
      edges: [...baseCatalog.edges, makeEdge('caller', 'read'), makeEdge('Order', 'data')],
    })
    const view = renderAt('/lineage?focus=Order')
    await user.click(screen.getByRole('button', { name: 'Add dependents hop' }))
    expect(graphIds()).toContain('caller')
    expect(screen.getByRole('combobox', { name: 'Dependencies hops' })).toHaveValue('1')
    expect(screen.getByRole('combobox', { name: 'Dependents hops' })).toHaveValue('2')
    await user.selectOptions(screen.getByRole('combobox', { name: 'Dependencies hops' }), '0')
    expect(graphIds()).not.toContain('data')
    await user.click(screen.getByRole('checkbox', { name: 'Group intermediate layers' }))
    const url = screen.getByTestId('location').textContent!
    expect(url).toContain('dependencies=0')
    expect(url).toContain('dependents=2')
    expect(url).toContain('group=intermediate')
    const ids = graphIds()
    view.unmount()
    renderAt(`/lineage${url}`)
    expect(graphIds()).toEqual(ids)
    expect(screen.getByTestId('graph')).toHaveAttribute('data-grouped', 'true')
  })
  it('keeps a filtered intermediate node when a later hop matches', async () => {
    const user = userEvent.setup()
    catalog = makeCatalog({
      ...baseCatalog,
      nodes: [...baseCatalog.nodes, makeNode({ id: 'report', type: 'StoredProcedures' })],
      edges: [...baseCatalog.edges, makeEdge('report', 'view')],
    })
    const { container } = renderAt('/lineage?focus=Order&hops=2')
    await addTypeFilter(user, container, 'StoredProcedures')
    expect(graphIds()).toContain('report')
    expect(graphIds()).toContain('view')
    expect(screen.getByText(/1 connecting object kept outside the filters/)).toBeInTheDocument()
  })
  it.each(['dependencies', 'dependents'] as const)(
    'expands %s beyond six hops and restores deep shared views',
    async (direction) => {
      const user = userEvent.setup()
      const ids = Array.from({ length: 22 }, (_, i) => `node${i}`)
      catalog = makeCatalog({
        nodes: ids.map((id) => makeNode({ id })),
        edges: ids
          .slice(1)
          .map((id, i) => (direction === 'dependencies' ? makeEdge(ids[i], id) : makeEdge(id, ids[i]))),
      })
      const otherDirection = direction === 'dependencies' ? 'dependents' : 'dependencies'
      const label = direction === 'dependencies' ? 'Dependencies' : 'Dependents'
      const view = renderAt(`/lineage?focus=node0&${direction}=6&${otherDirection}=0`)
      expect(graphIds()).toHaveLength(7)
      await user.click(screen.getByRole('button', { name: `Add ${direction} hop` }))
      expect(graphIds()).toContain('node7')
      expect(graphIds()).not.toContain('node8')
      await user.selectOptions(screen.getByRole('combobox', { name: `${label} hops` }), '20')
      expect(graphIds()).toHaveLength(21)
      expect(graphIds()).toContain('node20')
      expect(graphIds()).not.toContain('node21')
      expect(screen.getByRole('button', { name: `Add ${direction} hop` })).toBeDisabled()
      const url = screen.getByTestId('location').textContent!
      expect(url).toContain(`${direction}=20`)
      view.unmount()
      renderAt(`/lineage${url}`)
      expect(screen.getByRole('combobox', { name: `${label} hops` })).toHaveValue('20')
      expect(graphIds()).toEqual(ids.slice(0, 21))
    },
  )

  it('restores a legacy shared radius beyond six hops in both directions', () => {
    renderAt('/lineage?focus=Order&hops=12')
    expect(screen.getByRole('combobox', { name: 'Dependencies hops' })).toHaveValue('12')
    expect(screen.getByRole('combobox', { name: 'Dependents hops' })).toHaveValue('12')
  })

  it('validates hop limits and keeps old radius links working', () => {
    renderAt('/lineage?focus=Order&hops=3&dependencies=-1&dependents=100')
    expect(screen.getByRole('combobox', { name: 'Dependencies hops' })).toHaveValue('3')
    expect(screen.getByRole('combobox', { name: 'Dependents hops' })).toHaveValue('3')
  })
})
