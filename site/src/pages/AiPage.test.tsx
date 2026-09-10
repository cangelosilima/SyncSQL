import { act, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import AiPage from './AiPage'
import Explorer from './Explorer'
import { buildIndex } from '../lib/catalog'
import { makeCatalog, makeNode } from '../test/fixtures'
import type { FilterPlanV1, ModelLoadProgress } from '../ai/types'

const ai = vi.hoisted(() => ({ checking: false, available: true, reason: null as string | null, runtimeStatus: 'idle', progress: null as ModelLoadProgress | null, error: null as string | null, generateFilterPlan: vi.fn(), retry: vi.fn() }))
const plan: FilterPlanV1 = { version: 1, tokens: [{ attribute: 'type', operator: 'is', values: ['StoredProcedures'] }], contentQuery: 'Orders', confidence: 'medium', warnings: ['Review the interpretation.'], unsupportedFragments: [] }
vi.mock('../ai/AiContext', () => ({ useAi: () => ai }))
vi.mock('../lib/CatalogContext', () => ({ useCatalog: () => ({ index: buildIndex(makeCatalog({ nodes: [makeNode({ id: 'proc', type: 'StoredProcedures', ddl: 'SELECT * FROM Orders' }), makeNode({ id: 'table', ddl: 'Orders' })] })) }) }))
beforeEach(() => { Object.assign(ai, { checking: false, available: true, reason: null, runtimeStatus: 'idle', progress: null, error: null }); vi.clearAllMocks(); ai.generateFilterPlan.mockResolvedValue(plan) })
function View() { return <MemoryRouter initialEntries={['/ai']}><Routes><Route path="/ai" element={<AiPage />} /><Route path="/explorer" element={<Explorer />} /></Routes></MemoryRouter> }
function open() { return render(<View />) }
async function generate() { fireEvent.change(screen.getByRole('textbox'), { target: { value: 'Find procedures using Orders' } }); fireEvent.click(screen.getByRole('button', { name: 'Generate filters' })); await screen.findByText('medium confidence') }

describe('local AI presentation and Explorer transition', () => {
  it('opens the resolved column workspace instead of applying a DDL filter', async () => {
    ai.generateFilterPlan.mockResolvedValue({ ...plan, tokens: [], contentQuery: '', columnReference: { objectId: 'table', column: 'Id' } })
    open(); await generate()
    expect(screen.getByRole('heading', { name: 'Column references' })).toBeInTheDocument()
    expect(screen.getByText('0 object(s) with recorded references')).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Open column lineage' })).toHaveAttribute('href', '/object/table?column=Id')
    expect(screen.queryByRole('link', { name: 'Open in Explorer' })).not.toBeInTheDocument()
    expect(screen.queryByText(/DDL contains/)).not.toBeInTheDocument()
  })
  it('examples only fill input; generated filters and DDL preserve preview count in Explorer', async () => {
    open()
    fireEvent.click(screen.getByRole('button', { name: 'Show stored procedures in AppDb that mention Orders' }))
    expect(ai.generateFilterPlan).not.toHaveBeenCalled()
    await generate()
    expect(screen.getByText('1 of 2 object(s) match')).toBeInTheDocument()
    expect(screen.getByText('Review the interpretation.')).toBeInTheDocument()
    fireEvent.click(screen.getByRole('link', { name: 'Open in Explorer' }))
    expect(screen.getByRole('status')).toHaveTextContent('1 of 2 object(s) match')
    expect(screen.getByRole('button', { name: 'Remove filter DDL content contains Orders' })).toBeInTheDocument()
  })
  it('blocks transfer of unsupported fragments without discarding the explanation', async () => {
    ai.generateFilterPlan.mockResolvedValue({ ...plan, unsupportedFragments: ['owned by billing'] })
    open(); await generate()
    expect(screen.getByRole('button', { name: 'Open in Explorer' })).toBeDisabled()
    expect(screen.getByText(/owned by billing/)).toBeInTheDocument()
  })
  it('shows determinate and indeterminate model loading and cancellation aborts the request', async () => {
    let signal!: AbortSignal
    ai.generateFilterPlan.mockImplementation((_query, _nodes, nextSignal: AbortSignal) => { signal = nextSignal; return new Promise(() => {}) })
    const view = open()
    fireEvent.change(screen.getByRole('textbox'), { target: { value: 'Orders' } })
    fireEvent.click(screen.getByRole('button', { name: 'Generate filters' }))
    ai.runtimeStatus = 'loading'
    view.rerender(<View />)
    expect(screen.getByRole('progressbar')).not.toHaveAttribute('value')
    ai.progress = { file: 'model.onnx', percent: 47, status: 'progress' }
    view.rerender(<View />)
    expect(screen.getByRole('progressbar')).toHaveAttribute('value', '47')
    expect(screen.getByRole('textbox')).toBeDisabled()
    expect(signal.aborted).toBe(false)
    await act(async () => { fireEvent.click(screen.getByRole('button', { name: 'Cancel' })) })
    expect(signal.aborted).toBe(true)
  })
  it('keeps checking, runtime error details and retry visible', async () => {
    ai.checking = true
    const view = open()
    expect(screen.getByRole('status')).toHaveTextContent('Checking')
    Object.assign(ai, { checking: false, available: false, reason: 'runtime-error', error: 'Allocation failed' })
    view.rerender(<View />)
    expect(screen.getByText(/Allocation failed/)).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: 'Retry local model' }))
    await waitFor(() => expect(ai.retry).toHaveBeenCalledOnce())
  })
})
