import { fireEvent, render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { describe, expect, it, vi } from 'vitest'
import ObjectPage from './ObjectPage'
import { buildIndex } from '../lib/catalog'
import { makeCatalog, makeEdge, makeNode } from '../test/fixtures'

const orders = makeNode({
  id: 'orders',
  name: 'Orders',
  qualifiedName: 'dbo.Orders',
  columns: [
    { name: 'Id', dataType: 'int', description: null },
    { name: 'CustomerId', dataType: 'int', description: null },
    { name: 'Notes', dataType: 'nvarchar', description: null },
  ],
})
const report = makeNode({ id: 'report', name: 'Report', qualifiedName: 'dbo.Report', type: 'Views' })
const audit = makeNode({ id: 'audit', name: 'Audit', qualifiedName: 'dbo.Audit', type: 'StoredProcedures' })

const catalog = makeCatalog({
  nodes: [orders, report, audit],
  edges: [makeEdge('report', 'orders', ['CustomerId']), makeEdge('audit', 'orders', ['CustomerId'])],
  systemReferences: [{ from: 'orders', schema: null, name: 'sp_executesql' }],
})

vi.mock('../lib/CatalogContext', () => ({
  useCatalog: () => ({ loading: false, error: null, index: buildIndex(catalog) }),
}))

// react-flow needs layout measurements jsdom does not provide, and the column
// panel's assertions are about the list, not the picture.
vi.mock('../components/LineageGraph', () => ({
  default: ({ nodeIds }: { nodeIds: string[] }) => <div data-testid="graph">{nodeIds.join(',')}</div>,
}))

function renderObject(id: string, workspace = 'Columns') {
  const result = render(
    <MemoryRouter initialEntries={[`/object/${id}`]}>
      <Routes>
        <Route path="/object/*" element={<ObjectPage />} />
      </Routes>
    </MemoryRouter>,
  )
  fireEvent.click(screen.getByRole('tab', { name: workspace }))
  return result
}

function columnRow(name: string): HTMLElement {
  const cell = screen.getByRole('cell', { name })
  return cell.closest('tr') as HTMLElement
}

describe('ObjectPage column lineage', () => {
  it('shows how many objects read each column', () => {
    renderObject('orders')

    expect(within(columnRow('CustomerId')).getByRole('button', { name: /2 objects/ })).toBeInTheDocument()
    expect(within(columnRow('Notes')).getByRole('button', { name: /none/ })).toBeInTheDocument()
  })

  it('opens a panel listing the objects that read the clicked column', async () => {
    const user = userEvent.setup()
    const { container } = renderObject('orders')

    await user.click(within(columnRow('CustomerId')).getByRole('button', { name: /2 objects/ }))

    // Scoped to the panel: the page's own "Used by" list names these objects too.
    const panel = within(container.querySelector('.column-lineage-panel') as HTMLElement)
    expect(panel.getByRole('heading', { name: /Lineage for CustomerId/ })).toBeInTheDocument()
    expect(panel.getByRole('link', { name: 'dbo.Audit' })).toBeInTheDocument()
    expect(panel.getByRole('link', { name: 'dbo.Report' })).toBeInTheDocument()
    // The panel's graph is restricted to this column's readers.
    expect(panel.getByTestId('graph')).toHaveTextContent('orders,audit,report')
  })

  it('says so plainly when nothing is known to read the column', async () => {
    const user = userEvent.setup()
    renderObject('orders')

    await user.click(within(columnRow('Notes')).getByRole('button', { name: /none/ }))

    expect(screen.getByText(/No object in the catalog is known to reference/)).toBeInTheDocument()
  })

  it('opens the column named in ?column= without a click', () => {
    render(
      <MemoryRouter initialEntries={['/object/orders?column=CustomerId']}>
        <Routes>
          <Route path="/object/*" element={<ObjectPage />} />
        </Routes>
      </MemoryRouter>,
    )

    expect(screen.getByRole('heading', { name: /Lineage for CustomerId/ })).toBeInTheDocument()
  })
})

describe('ObjectPage system references', () => {
  it('lists engine-provided objects separately from orphaned references', () => {
    const { container } = renderObject('orders', 'Graph')

    expect(screen.getByRole('heading', { name: 'System objects referenced' })).toBeInTheDocument()
    // Named twice on the page - once in the section's explanation, once as the
    // tag itself; the tag is the one that carries the data.
    expect(screen.getAllByText('sp_executesql').some((el) => el.classList.contains('column-tag'))).toBe(true)
    // And crucially, no orphaned-reference warning: sp_executesql is not missing.
    expect(container.querySelector('.orphaned-ref-warning')).toBeNull()
  })
})

describe('ObjectPage workbook export', () => {
  it('offers a whole-object XLSX download alongside the per-section CSVs', () => {
    renderObject('orders')

    expect(screen.getByRole('button', { name: 'Export XLSX' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Export details CSV' })).toBeInTheDocument()
  })
})
