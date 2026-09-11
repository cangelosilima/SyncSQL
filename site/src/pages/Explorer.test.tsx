import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, useLocation } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import Explorer from './Explorer'
import { buildIndex } from '../lib/catalog'
import { makeCatalog, makeNode } from '../test/fixtures'
import { encodeTokensForUrl } from '../lib/filters'
import { downloadCsv } from '../lib/csv'

let nodes = [makeNode({ id: 'a' })]
vi.mock('../lib/CatalogContext', () => ({ useCatalog: () => ({ index: buildIndex(makeCatalog({ nodes })) }) }))
vi.mock('../lib/csv', async (original) => ({
  ...(await original<typeof import('../lib/csv')>()),
  downloadCsv: vi.fn(),
}))
function Location() {
  return <span data-testid="url">{useLocation().search}</span>
}
function open(query = '') {
  render(
    <MemoryRouter initialEntries={['/explorer' + query]}>
      <Explorer />
      <Location />
    </MemoryRouter>,
  )
}
beforeEach(() => vi.clearAllMocks())

describe('Explorer workbench parity', () => {
  it.each(['DDL', 'content', 'name', 'type'])(
    'commits %s as a broad search without selecting a matching attribute',
    async (query) => {
      const user = userEvent.setup()
      nodes = [makeNode({ id: 'match', ddl: `SELECT ${query}` }), makeNode({ id: 'other', ddl: '' })]
      open()
      const input = screen.getByRole('textbox')
      await user.type(input, query)
      expect(screen.getAllByRole('option').length).toBeGreaterThan(0)
      await user.keyboard('{Enter}')
      expect(screen.getByRole('button', { name: `Remove filter ${query}` })).toBeInTheDocument()
      expect(screen.getByRole('status')).toHaveTextContent('1 of 2')
      expect(screen.getByRole('link', { name: nodes[0].qualifiedName })).toBeInTheDocument()
    },
  )

  it('selects attributes with arrow keys and Enter, then returns to broad search after committing', async () => {
    const user = userEvent.setup()
    nodes = [makeNode({ id: 'match', ddl: 'SELECT DDL' }), makeNode({ id: 'other', ddl: '' })]
    open()
    const input = screen.getByRole('textbox')
    await user.type(input, 'd{ArrowDown}{Enter}')
    expect(input).toHaveAttribute('placeholder', 'Database...')
    await user.keyboard('{Backspace}')
    await user.type(input, 'DDL{ArrowUp}{Enter}')
    expect(input).toHaveAttribute('placeholder', 'DDL content...')
    await user.type(input, 'contains{Enter}DDL{Enter}')
    expect(screen.getByRole('button', { name: 'Remove filter DDL content contains DDL' })).toBeInTheDocument()
    await user.type(input, 'DDL{Enter}')
    expect(screen.getByRole('button', { name: 'Remove filter DDL' })).toBeInTheDocument()
    expect(screen.getByRole('status')).toHaveTextContent('1 of 2')
  })

  it('clears the suggestion selection when the search text changes', async () => {
    const user = userEvent.setup()
    nodes = [makeNode({ id: 'match', ddl: 'SELECT DDL' }), makeNode({ id: 'other', ddl: '' })]
    open()
    await user.type(screen.getByRole('textbox'), 'DD{ArrowDown}L{Enter}')
    expect(screen.getByRole('button', { name: 'Remove filter DDL' })).toBeInTheDocument()
    expect(screen.getByRole('status')).toHaveTextContent('1 of 2')
  })

  it('limits visible rows to 500 but exports all 501 matches in the selected sort order', () => {
    nodes = Array.from({ length: 501 }, (_, i) =>
      makeNode({ id: String(i), qualifiedName: `dbo.Object${String(i).padStart(3, '0')}`, changeCount: i }),
    )
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
    nodes = [
      makeNode({ id: 'a', type: 'Views', sections: [{ title: 'Extra', content: 'ArchivedOrders' }] }),
      makeNode({ id: 'b', type: 'Tables', ddl: 'ArchivedOrders' }),
    ]
    const filters = encodeTokensForUrl([
      { attribute: 'type', operator: 'is', values: ['Views'] },
      { attribute: 'database', operator: 'is', values: ['AppDb'] },
    ])
    open(`?filters=${encodeURIComponent(filters)}&q=ArchivedOrders&keep=yes`)
    expect(screen.getByRole('status')).toHaveTextContent('1 of 2 object(s) match')
    expect(screen.getAllByRole('textbox')).toHaveLength(1)
    fireEvent.click(screen.getByRole('button', { name: 'Remove filter DDL content contains ArchivedOrders' }))
    const input = screen.getByRole('textbox')
    fireEvent.change(input, { target: { value: 'missing' } })
    fireEvent.keyDown(input, { key: 'Enter' })
    await waitFor(() => expect(screen.getByRole('status')).toHaveTextContent('0 of 2'))
    expect(screen.getByTestId('url')).toHaveTextContent('keep=yes')
    expect(decodeURIComponent(screen.getByTestId('url').textContent ?? '')).toContain('missing')
    expect(screen.getByTestId('url')).not.toHaveTextContent('q=')
    expect(screen.getByText('No objects match this filter.')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Export CSV' })).toBeDisabled()
    expect(screen.getByRole('button', { name: 'Remove filter missing' })).toBeInTheDocument()
  })

  it('searches metadata and SQL in one field and combines a DDL filter with other criteria', async () => {
    const user = userEvent.setup()
    nodes = [
      makeNode({ id: 'name', qualifiedName: 'dbo.Needle', ddl: '', type: 'Views' }),
      makeNode({ id: 'sql', ddl: 'SELECT Needle', type: 'Views' }),
      makeNode({ id: 'section', ddl: '', sections: [{ title: 'Index', content: 'Needle' }] }),
      makeNode({ id: 'other', ddl: '' }),
    ]
    open()
    const input = screen.getByRole('textbox')
    await user.type(input, 'Needle{Enter}')
    expect(screen.getByRole('status')).toHaveTextContent('3 of 4')
    await user.click(screen.getByRole('button', { name: 'Remove filter Needle' }))
    await user.click(input)
    await user.click(screen.getByRole('option', { name: 'DDL content' }))
    await user.click(screen.getByRole('option', { name: 'contains' }))
    await user.type(input, 'Needle{Enter}')
    expect(screen.getByRole('status')).toHaveTextContent('2 of 4')
    await user.click(screen.getByRole('option', { name: 'Type' }))
    await user.click(screen.getByRole('option', { name: 'is' }))
    await user.click(screen.getByRole('option', { name: 'Views' }))
    expect(screen.getByRole('status')).toHaveTextContent('1 of 4')
    expect(screen.getByRole('link', { name: nodes[1].qualifiedName })).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Remove filter DDL content contains Needle' }))
    expect(screen.getByRole('status')).toHaveTextContent('2 of 4')
  })
})
