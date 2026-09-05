import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { describe, expect, it, vi } from 'vitest'
import LineagePage from './LineagePage'
import { buildIndex } from '../lib/catalog'
import { makeCatalog, makeEdge, makeNode } from '../test/fixtures'

/**
 * A table with three neighbours of two different types - the shape the reported
 * bug shows up on: drilling into the table, then asking for just the stored
 * procedures around it.
 */
const boleta = makeNode({ id: 'boleta', name: 'Opn_Boleta', qualifiedName: 'dbo.Opn_Boleta', type: 'Tables' })
const readProc = makeNode({ id: 'read', name: 'ReadBoleta', qualifiedName: 'dbo.ReadBoleta', type: 'StoredProcedures' })
const writeProc = makeNode({ id: 'write', name: 'WriteBoleta', qualifiedName: 'dbo.WriteBoleta', type: 'StoredProcedures' })
const summaryView = makeNode({ id: 'view', name: 'BoletaSummary', qualifiedName: 'dbo.BoletaSummary', type: 'Views' })

const catalog = makeCatalog({
  nodes: [boleta, readProc, writeProc, summaryView],
  edges: [makeEdge('read', 'boleta'), makeEdge('write', 'boleta'), makeEdge('view', 'boleta')],
})

vi.mock('../lib/CatalogContext', () => ({
  useCatalog: () => ({ loading: false, error: null, index: buildIndex(catalog) }),
}))

// The graph is react-flow + dagre, which needs layout measurements jsdom does
// not provide. The assertions here are about which node ids the page selects,
// so the graph is stubbed down to a list of the names it was handed.
vi.mock('../components/LineageGraph', () => ({
  default: ({ nodeIds }: { nodeIds: string[] }) => (
    <div data-testid="graph">{nodeIds.join(',')}</div>
  ),
}))

function renderAt(entry: string) {
  return render(
    <MemoryRouter initialEntries={[entry]}>
      <Routes>
        <Route path="/lineage" element={<LineagePage />} />
      </Routes>
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
  it('shows the focused object and its neighbours when arriving with ?focus=', () => {
    renderAt('/lineage?focus=boleta')

    expect(graphIds().sort()).toEqual(['boleta', 'read', 'view', 'write'])
  })

  it('does not seed a name filter token from ?focus=', () => {
    renderAt('/lineage?focus=boleta')

    // The chip is what used to make every later filter contradictory.
    expect(screen.queryByText(/Name is dbo\.Opn_Boleta/i)).not.toBeInTheDocument()
  })

  /**
   * The reported bug: navigating into dbo.Opn_Boleta and adding "Type is
   * StoredProcedures" used to drop the focus and AND both tokens against the
   * whole catalog, which asked for an object that was both the table and a
   * stored procedure - so the graph emptied.
   */
  it('narrows the focused neighbourhood by type instead of emptying the graph', async () => {
    const user = userEvent.setup()
    const { container } = renderAt('/lineage?focus=boleta')

    await addTypeFilter(user, container, 'StoredProcedures')

    const ids = graphIds().sort()
    expect(ids).toContain('boleta')
    expect(ids).toContain('read')
    expect(ids).toContain('write')
    expect(ids).not.toContain('view')
  })

  it('keeps the focus when a filter is added', async () => {
    const user = userEvent.setup()
    const { container } = renderAt('/lineage?focus=boleta')

    await addTypeFilter(user, container, 'StoredProcedures')

    expect(screen.getByText('Navigating:')).toBeInTheDocument()
    expect(screen.getByText(/Showing 3 of 4 objects around dbo\.Opn_Boleta/)).toBeInTheDocument()
  })

  it('turns the navigation into a name filter when focus is cleared', async () => {
    const user = userEvent.setup()
    renderAt('/lineage?focus=boleta')

    await user.click(screen.getByRole('button', { name: /Clear focus/i }))

    // Not back to the whole catalog - still looking at that one object.
    expect(graphIds()).toEqual(['boleta'])
    expect(screen.getByText(/Name is dbo\.Opn_Boleta/i)).toBeInTheDocument()
  })

  it('selects from the whole catalog when nothing is focused', () => {
    renderAt('/lineage')

    expect(graphIds().sort()).toEqual(['boleta', 'read', 'view', 'write'])
  })
})
