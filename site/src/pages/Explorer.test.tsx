import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { MemoryRouter, useLocation } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import Explorer from './Explorer'
import { buildIndex } from '../lib/catalog'
import { makeCatalog, makeNode } from '../test/fixtures'
import { encodeTokensForUrl } from '../lib/filters'
import { downloadCsv } from '../lib/csv'

let nodes = [makeNode({ id: 'a' })]
vi.mock('../lib/CatalogContext', () => ({ useCatalog: () => ({ index: buildIndex(makeCatalog({ nodes })) }) }))
vi.mock('../lib/csv', async (original) => ({ ...await original<typeof import('../lib/csv')>(), downloadCsv: vi.fn() }))
function Location() { return <span data-testid="url">{useLocation().search}</span> }
function open(query = '') { render(<MemoryRouter initialEntries={['/explorer' + query]}><Explorer /><Location /></MemoryRouter>) }
beforeEach(() => vi.clearAllMocks())

describe('Explorer workbench parity', () => {
  it('limits visible rows to 500 but exports all 501 matches in the selected sort order', () => {
    nodes = Array.from({ length: 501 }, (_, i) => makeNode({ id: String(i), qualifiedName: `dbo.Object${String(i).padStart(3, '0')}`, changeCount: i }))
    open()
    expect(within(screen.getByRole('table')).getAllByRole('row')).toHaveLength(501)
    expect(screen.getByText(/Showing the first 500 of 501/)).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: 'Changes' }))
    fireEvent.click(screen.getByRole('button', { name: /Changes/ }))
    expect(screen.getByRole('columnheader', { name: /Changes/ })).toHaveAttribute('aria-sort', 'descending')
    fireEvent.click(screen.getByRole('button', { name: 'Export CSV' }))
    const csv = vi.mocked(downloadCsv).mock.calls[0][0]
    expect(csv.trim().split(/\r?\n/)).toHaveLength(502)
    expect(csv.indexOf('dbo.Object500')).toBeLessThan(csv.indexOf('dbo.Object000'))
  })

  it('restores AND tokens plus appended-DDL search, preserves unrelated URL state and keeps unmatched criteria', async () => {
    nodes = [makeNode({ id: 'a', type: 'Views', sections: [{ title: 'Extra', content: 'ArchivedOrders' }] }), makeNode({ id: 'b', type: 'Tables', ddl: 'ArchivedOrders' })]
    const filters = encodeTokensForUrl([{ attribute: 'type', operator: 'is', values: ['Views'] }, { attribute: 'database', operator: 'is', values: ['AppDb'] }])
    open(`?filters=${encodeURIComponent(filters)}&q=ArchivedOrders&keep=yes`)
    expect(screen.getByRole('status')).toHaveTextContent('1 of 2 object(s) match')
    fireEvent.change(screen.getByRole('textbox', { name: 'Search DDL content' }), { target: { value: 'missing' } })
    await waitFor(() => expect(screen.getByRole('status')).toHaveTextContent('0 of 2'))
    expect(screen.getByTestId('url')).toHaveTextContent('keep=yes')
    expect(screen.getByTestId('url')).toHaveTextContent('q=missing')
    expect(screen.getByText('No objects match this filter.')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Export CSV' })).toBeDisabled()
    expect(screen.getByRole('textbox', { name: 'Search DDL content' })).toHaveValue('missing')
  })
})
