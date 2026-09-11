import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter, useLocation } from 'react-router-dom'
import { expect, it, vi } from 'vitest'
import LineagePage from './LineagePage'
import { buildIndex } from '../lib/catalog'
import { makeCatalog, makeNode } from '../test/fixtures'
import { downloadCsv } from '../lib/csv'
import { encodeTokensForUrl, type FilterTokenInput } from '../lib/filters'

const nodes = [
  makeNode({
    id: 'orders',
    ddl: 'SELECT private_marker',
    grants: [
      { grantee: 'app_reader', granteeType: 'DATABASE_ROLE', permission: 'SELECT', state: 'GRANT', column: null },
      { grantee: 'app_reader', granteeType: 'DATABASE_ROLE', permission: 'UPDATE', state: 'DENY', column: 'Total' },
    ],
  }),
  makeNode({
    id: 'users',
    ddl: 'SELECT public_marker',
    grants: [
      { grantee: 'app_reader_extra', granteeType: 'DATABASE_ROLE', permission: 'SELECT', state: 'GRANT', column: null },
    ],
  }),
]
vi.mock('../lib/CatalogContext', () => ({ useCatalog: () => ({ index: buildIndex(makeCatalog({ nodes })) }) }))
vi.mock('../lib/csv', async (original) => ({
  ...(await original<typeof import('../lib/csv')>()),
  downloadCsv: vi.fn(),
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
function Location() {
  return <span data-testid="location">{useLocation().search}</span>
}
function show(url: string) {
  return render(
    <MemoryRouter initialEntries={[url]}>
      <LineagePage />
      <Location />
    </MemoryRouter>,
  )
}

it('migrates legacy exact user links into one filter and preserves permission export and focus', () => {
  show('/lineage?tab=access&grantee=app_reader&exact=1')
  expect(screen.getByText('Grantee is app_reader')).toBeVisible()
  expect(screen.queryByRole('button', { name: 'Browse' })).not.toBeInTheDocument()
  expect(screen.queryByRole('button', { name: 'Access' })).not.toBeInTheDocument()
  expect(screen.getAllByRole('textbox')).toHaveLength(1)
  expect(screen.getByText(/2 grants across 1 object/)).toBeVisible()
  fireEvent.click(screen.getByRole('button', { name: 'Export CSV' }))
  const csv = vi.mocked(downloadCsv).mock.calls.slice(-1)[0][0]
  expect(csv.trim().split(/\r?\n/)).toHaveLength(3)
  expect(csv).toContain('DENY')
  expect(csv).toContain('Total')
  expect(csv).not.toContain('app_reader_extra')
  fireEvent.click(screen.getByRole('button', { name: 'Focus orders' }))
  expect(screen.getByRole('heading', { name: 'Recorded permissions' })).toBeVisible()
  expect(screen.getByTestId('location')).toHaveTextContent('focus=orders')
  expect(decodeURIComponent(screen.getByTestId('location').textContent!.replace(/\+/g, ' '))).toContain('grantee')
})

it('combines old DDL and partial grantee searches and clears each chip independently', () => {
  show('/lineage?tab=access&grantee=app_reader&q=private_marker')
  expect(screen.getByText('DDL content contains private_marker')).toBeVisible()
  expect(screen.queryByRole('button', { name: 'Focus users' })).not.toBeInTheDocument()
  fireEvent.click(screen.getByRole('button', { name: 'Remove filter DDL content contains private_marker' }))
  expect(screen.getByText(/3 grants across 2 objects/)).toBeVisible()
  fireEvent.click(screen.getByRole('button', { name: 'Remove filter Grantee contains app_reader' }))
  expect(screen.queryByRole('button', { name: 'Export CSV' })).not.toBeInTheDocument()
  expect(screen.getByRole('button', { name: 'Focus users' })).toBeVisible()
})

it('restores combined filters and limits CSV to the same results', () => {
  const tokens: FilterTokenInput[] = [
    { attribute: 'grantee', operator: 'contains', values: ['app_reader'] },
    { attribute: 'ddl', operator: 'contains', values: ['public_marker'] },
  ]
  show(`/lineage?filter=${encodeURIComponent(encodeTokensForUrl(tokens))}`)
  expect(screen.getByText(/1 grant across 1 object/)).toBeVisible()
  expect(screen.queryByRole('button', { name: 'Focus orders' })).not.toBeInTheDocument()
  fireEvent.click(screen.getByRole('button', { name: 'Export CSV' }))
  const csv = vi.mocked(downloadCsv).mock.calls.slice(-1)[0][0]
  expect(csv.trim().split(/\r?\n/)).toHaveLength(2)
  expect(csv).toContain('app_reader_extra')
})

it('offers grantees in the shared filter and supports keyboard selection', async () => {
  show('/lineage')
  const input = screen.getByRole('textbox')
  fireEvent.focus(input)
  fireEvent.mouseDown(screen.getByRole('option', { name: 'Grantee' }))
  fireEvent.mouseDown(screen.getByRole('option', { name: 'is' }))
  fireEvent.change(input, { target: { value: 'app_reader' } })
  await waitFor(() => expect(screen.getByRole('option', { name: 'app_reader' })).toBeVisible())
  fireEvent.keyDown(input, { key: 'Enter' })
  expect(screen.getByText('Grantee is app_reader')).toBeVisible()
  expect(screen.getByText(/2 grants across 1 object/)).toBeVisible()
})

it('places the collapsed legend after the graph with a visible reminder', () => {
  const { container } = show('/lineage')
  const legend = container.querySelector('details.lineage-legend')!
  expect(legend).not.toHaveAttribute('open')
  expect(legend.querySelector('summary')).toHaveTextContent('Graph legend')
  expect(legend.querySelector('summary')).toHaveTextContent('dashed = dynamic SQL')
  expect(
    screen.getByRole('button', { name: 'Focus orders' }).compareDocumentPosition(legend) &
      Node.DOCUMENT_POSITION_FOLLOWING,
  ).toBeTruthy()
})
