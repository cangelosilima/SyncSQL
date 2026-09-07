import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter, useLocation } from 'react-router-dom'
import { describe, expect, it, vi } from 'vitest'
import LineagePage from './LineagePage'
import { buildIndex } from '../lib/catalog'
import { makeCatalog, makeNode } from '../test/fixtures'
import { downloadCsv } from '../lib/csv'

const nodes = [makeNode({ id: 'orders', grants: [
  { grantee: 'app_reader', granteeType: 'DATABASE_ROLE', permission: 'SELECT', state: 'GRANT', column: null },
  { grantee: 'app_reader', granteeType: 'DATABASE_ROLE', permission: 'UPDATE', state: 'DENY', column: 'Total' },
] }), makeNode({ id: 'users', grants: [{ grantee: 'app_reader_extra', granteeType: 'DATABASE_ROLE', permission: 'SELECT', state: 'GRANT', column: null }] })]
vi.mock('../lib/CatalogContext', () => ({ useCatalog: () => ({ index: buildIndex(makeCatalog({ nodes })) }) }))
vi.mock('../lib/csv', async (original) => ({ ...await original<typeof import('../lib/csv')>(), downloadCsv: vi.fn() }))
vi.mock('../components/LineageGraph', () => ({ default: ({ nodeIds, onNodeActivate }: { nodeIds: string[]; onNodeActivate: (id: string) => void }) => <div>{nodeIds.map(id => <button key={id} onClick={() => onNodeActivate(id)}>Focus {id}</button>)}</div> }))
function Location() { return <span data-testid="location">{useLocation().search}</span> }

describe('Access investigation', () => {
  it('preserves exact matching, per-permission CSV, focus and inspector URL independence', async () => {
    render(<MemoryRouter initialEntries={['/lineage?tab=access&grantee=app_reader']}><LineagePage /><Location /></MemoryRouter>)
    expect(screen.getByText(/3 grants across 2 objects/)).toBeInTheDocument()
    fireEvent.click(screen.getByRole('checkbox', { name: 'Exact match' }))
    expect(screen.getByText(/2 grants across 1 object/)).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: 'Export CSV' }))
    const csv = vi.mocked(downloadCsv).mock.calls[0][0]
    expect(csv.trim().split(/\r?\n/)).toHaveLength(3)
    expect(csv).toContain('DENY'); expect(csv).toContain('Total')
    fireEvent.click(screen.getByRole('button', { name: 'Focus orders' }))
    const url = screen.getByTestId('location').textContent
    expect(screen.getByRole('complementary', { name: 'Inspector' })).toBeVisible()
    expect(screen.getByRole('heading', { name: 'Recorded permissions' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Inspector' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Close inspector' })).not.toBeInTheDocument()
    fireEvent.keyDown(screen.getByRole('complementary'), { key: 'Escape' })
    expect(screen.getByRole('complementary')).toBeVisible()
    expect(screen.getByTestId('location')).toHaveTextContent(url!)
    expect(screen.getByText('Navigating:')).toBeInTheDocument()
    expect(screen.getByRole('checkbox')).toBeChecked()
  })
  it('allows keyboard selection of a grantee suggestion', async () => {
    render(<MemoryRouter initialEntries={['/lineage?tab=access']}><LineagePage /></MemoryRouter>)
    const input = screen.getByRole('combobox')
    fireEvent.change(input, { target: { value: 'app_' } })
    await waitFor(() => expect(screen.getAllByRole('option').length).toBe(2))
    fireEvent.keyDown(input, { key: 'Enter' })
    expect(input).toHaveValue('app_reader')
    expect(input).toHaveAttribute('aria-expanded', 'false')
  })
})
