import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import LineagePage from './LineagePage'
import { buildIndex } from '../lib/catalog'
import { makeCatalog, makeEdge, makeNode } from '../test/fixtures'

/**
 * A table with three neighbours of two different types - the shape the reported
 * bug shows up on: drilling into the table, then asking for just the stored
 * procedures around it.
 */
const Order = makeNode({ id: 'Order', name: 'Order', qualifiedName: 'dbo.Order', type: 'Tables' })
const readProc = makeNode({ id: 'read', name: 'ReadOrder', qualifiedName: 'dbo.ReadOrder', type: 'StoredProcedures' })
const writeProc = makeNode({ id: 'write', name: 'WriteOrder', qualifiedName: 'dbo.WriteOrder', type: 'StoredProcedures' })
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
  default: ({ nodeIds, groupIntermediate }: { nodeIds: string[]; groupIntermediate?: boolean }) => (
    <div data-testid="graph" data-grouped={String(groupIntermediate)}>{nodeIds.join(',')}</div>
  ),
}))

function Location() { return <span data-testid="location">{useLocation().search}</span> }

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
  beforeEach(() => { catalog = baseCatalog })
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
    catalog = makeCatalog({ ...baseCatalog,
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
    catalog = makeCatalog({ ...baseCatalog,
      nodes: [...baseCatalog.nodes, makeNode({ id: 'report', type: 'StoredProcedures' })],
      edges: [...baseCatalog.edges, makeEdge('report', 'view')],
    })
    const { container } = renderAt('/lineage?focus=Order&hops=2')
    await addTypeFilter(user, container, 'StoredProcedures')
    expect(graphIds()).toContain('report')
    expect(graphIds()).toContain('view')
    expect(screen.getByText(/1 connecting object kept outside the filters/)).toBeInTheDocument()
  })
  it('validates hop limits and keeps old radius links working', () => {
    renderAt('/lineage?focus=Order&hops=3&dependencies=-1&dependents=100')
    expect(screen.getByRole('combobox', { name: 'Dependencies hops' })).toHaveValue('3')
    expect(screen.getByRole('combobox', { name: 'Dependents hops' })).toHaveValue('3')
  })
})
