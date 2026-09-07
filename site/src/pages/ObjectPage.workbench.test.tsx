import { fireEvent, render, screen, within } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { describe, expect, it, vi } from 'vitest'
import ObjectPage from './ObjectPage'
import { makeCatalog, makeNode } from '../test/fixtures'
import { buildIndex } from '../lib/catalog'

const object = makeNode({ id: 'orders', ddl: 'CURRENT SQL', sections: [{ title: 'Constraints', content: 'CURRENT CONSTRAINT' }], history: [
  { sha: 'oldsha1', date: '2026-01-01', message: 'Old revision', ddl: 'OLD SQL' },
  { sha: 'newsha2', date: '2026-02-01', message: 'New revision', ddl: 'NEW SQL' },
  { sha: 'missing', date: '2026-03-01', message: 'Missing revision', ddl: null },
] })
vi.mock('../lib/CatalogContext', () => ({ useCatalog: () => ({ index: buildIndex(makeCatalog({ nodes: [object] })) }) }))
vi.mock('../components/CodeBlock', () => ({ default: ({ code }: { code: string }) => <pre>{code}</pre> }))
vi.mock('../components/DiffView', () => ({ default: ({ oldText, newText }: { oldText: string; newText: string }) => <output data-testid="diff">{oldText} → {newText}</output> }))
function tab(name: string) { fireEvent.click(screen.getByRole('tab', { name })) }
function open() { render(<MemoryRouter initialEntries={['/object/orders']}><Routes><Route path="/object/*" element={<ObjectPage />} /></Routes></MemoryRouter>) }
function panel() { return within(screen.getByRole('tabpanel')) }

describe('Object workspace state lifetime', () => {
  it('keeps historical SQL across workspace changes and returns appended sections only on latest', () => {
    open()
    tab('History')
    expect(panel().getByRole('button', { name: /Missing revision/ })).toBeDisabled()
    fireEvent.click(panel().getByRole('button', { name: /Old revision/ }))
    expect(within(screen.getByRole('region', { name: 'Definition' })).getByText('OLD SQL')).toBeInTheDocument()
    expect(screen.getByRole('status')).toHaveTextContent('current catalog snapshot')
    tab('Access')
    expect(panel().getByText(/No grants are recorded/)).toBeInTheDocument()
    tab('Columns')
    expect(within(screen.getByRole('region', { name: 'Definition' })).getByText('OLD SQL')).toBeInTheDocument()
    expect(within(screen.getByRole('region', { name: 'Definition' })).queryByText('Constraints')).not.toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: 'Back to latest' }))
    expect(within(screen.getByRole('region', { name: 'Definition' })).getByText('CURRENT SQL')).toBeInTheDocument()
    expect(within(screen.getByRole('region', { name: 'Definition' })).getByText('Constraints')).toBeInTheDocument()
  })

  it('keeps comparison picks across workspace changes and orders revisions by time', () => {
    open(); tab('History')
    fireEvent.click(panel().getByRole('button', { name: 'Compare two revisions' }))
    expect(screen.getByRole('tab', { name: 'Diff' })).toHaveAttribute('aria-selected', 'true')
    fireEvent.click(panel().getByRole('button', { name: /New revision/ }))
    tab('Metrics'); tab('Diff')
    fireEvent.click(panel().getByRole('button', { name: /Old revision/ }))
    expect(screen.getByTestId('diff')).toHaveTextContent('OLD SQL → NEW SQL')
    tab('Columns'); tab('Diff')
    expect(screen.getByTestId('diff')).toHaveTextContent('OLD SQL → NEW SQL')
    fireEvent.click(panel().getByRole('button', { name: /Current definition/ }))
    expect(screen.getByTestId('diff')).toHaveTextContent('OLD SQL → CURRENT SQL')
  })

  it('supports keyboard workspace navigation and exposes Relationships between Access and Metrics', () => {
    open()
    const definition = screen.getByRole('tab', { name: 'Columns' })
    definition.focus(); fireEvent.keyDown(definition, { key: 'ArrowRight' })
    expect(screen.getByRole('tab', { name: 'Graph' })).toHaveFocus()
    expect(screen.getByRole('region', { name: 'Definition' })).toBeVisible()
    expect(screen.getByRole('tab', { name: 'Graph' })).toHaveAttribute('aria-selected', 'true')
    expect(screen.queryByRole('complementary')).not.toBeInTheDocument()
    const tabs = screen.getAllByRole('tab').map(element => element.textContent)
    expect(tabs.slice(2, 5)).toEqual(['Access', 'Relationships', 'Metrics'])
    tab('Relationships')
    expect(panel().queryByRole('heading', { name: 'Properties' })).not.toBeInTheDocument()
    expect(screen.queryByRole('region', { name: 'Properties' })).not.toBeInTheDocument()
    fireEvent.click(panel().getByRole('button', { name: 'All relationship evidence' }))
    expect(screen.getByRole('tab', { name: 'Graph' })).toHaveAttribute('aria-selected', 'true')
    expect(screen.queryByRole('region', { name: 'Properties' })).not.toBeInTheDocument()
  })
})
