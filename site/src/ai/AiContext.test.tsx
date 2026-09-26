import { act, fireEvent, render, renderHook, screen, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { AiProvider, parseCapabilityManifest, useAi } from './AiContext'
import { EmbeddingFilterPlanner } from './filterPlanner'

const worker = vi.hoisted(() => ({
  load: vi.fn(),
  embed: vi.fn(),
  dispose: vi.fn(),
}))

vi.mock('./WorkerEmbeddingAdapter', () => ({
  WorkerEmbeddingAdapter: class {
    load = worker.load
    embed = worker.embed
    dispose = worker.dispose
  },
}))

describe('parseCapabilityManifest', () => {
  it.each([
    'model-missing',
    'checksum-mismatch',
    'packaging-failed',
    'manifest-unavailable',
    'runtime-error',
    'invalid',
  ])('validates deployment reason %s', (reason) => {
    expect(parseCapabilityManifest({ filterGenerator: { available: false, reason } }).filterGenerator.reason).toBe(
      reason === 'invalid' ? 'manifest-unavailable' : reason,
    )
  })
  it.each(['bad', { filterGenerator: 'bad' }, { filterGenerator: { available: true, model: 'model', version: 2 } }])(
    'rejects malformed capability %s',
    (value) => {
      expect(parseCapabilityManifest(value).filterGenerator.available).toBe(false)
    },
  )
  it('accepts a valid filter-generator capability', () => {
    expect(
      parseCapabilityManifest({
        filterGenerator: { available: true, model: 'all-MiniLM-L6-v2', version: 1 },
      }),
    ).toEqual({
      filterGenerator: { available: true, model: 'all-MiniLM-L6-v2', version: 1 },
    })
  })

  it.each([undefined, null, {}, { filterGenerator: { available: true, version: 1 } }])(
    'defaults malformed manifests to unavailable',
    (value) => {
      expect(parseCapabilityManifest(value).filterGenerator.available).toBe(false)
      expect(parseCapabilityManifest(value).filterGenerator.reason).toBe('manifest-unavailable')
    },
  )

  it('preserves a safe deployment reason code', () => {
    expect(
      parseCapabilityManifest({
        filterGenerator: { available: false, reason: 'lfs-unresolved', version: 1 },
      }).filterGenerator.reason,
    ).toBe('lfs-unresolved')
  })
})

describe('AiProvider runtime failure handling', () => {
  beforeEach(() => {
    worker.load.mockReset()
    worker.embed.mockReset()
    worker.dispose.mockReset()
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue({
        ok: true,
        json: () =>
          Promise.resolve({
            filterGenerator: { available: true, model: 'all-MiniLM-L6-v2', version: 1 },
          }),
      }),
    )
  })

  it('exposes safe defaults outside a provider', async () => {
    const { result } = renderHook(useAi)
    await expect(result.current.generateFilterPlan('', [])).rejects.toThrow('not ready')
    expect(result.current.retry()).toBeUndefined()
  })

  it.each([new Error('offline'), 'offline', null])('reports manifest failures (%s)', async (failure) => {
    vi.mocked(fetch).mockReset()
    if (failure === null) vi.mocked(fetch).mockResolvedValue({ ok: false, status: 404 } as Response)
    else vi.mocked(fetch).mockRejectedValue(failure)
    const { result } = renderHook(useAi, { wrapper: AiProvider })
    await waitFor(() => expect(result.current.checking).toBe(false))
    expect(result.current.reason).toBe('manifest-unavailable')
    expect(result.current.error).toBe(failure === null ? 'Capability manifest returned 404.' : 'offline')
    await expect(result.current.generateFilterPlan('', [])).rejects.toThrow('unavailable')
    act(() => result.current.retry())
  })

  it('ignores a rejected manifest request after unmount', async () => {
    let reject!: (reason: Error) => void
    vi.mocked(fetch).mockImplementation(
      () =>
        new Promise((_resolve, no) => {
          reject = no
        }),
    )
    const { unmount, result } = renderHook(useAi, { wrapper: AiProvider })
    unmount()
    await act(async () => reject(new Error('aborted')))
    expect(result.current.error).toBeNull()
  })

  it('generates plans, reuses the adapter, publishes progress, and disposes on unmount', async () => {
    const plan = { version: 1, filters: [] }
    const generate = vi.spyOn(EmbeddingFilterPlanner.prototype, 'generate').mockResolvedValue(plan as never)
    worker.load.mockImplementation(async ({ onProgress }) => onProgress({ percent: 100, file: null, status: 'ready' }))
    const { result, unmount } = renderHook(useAi, { wrapper: AiProvider })
    await waitFor(() => expect(result.current.available).toBe(true))
    await act(async () => {
      expect(await result.current.generateFilterPlan('tables', [])).toBe(plan)
    })
    await act(async () => {
      await result.current.generateFilterPlan('tables', [])
    })
    expect(result.current.runtimeStatus).toBe('idle')
    expect(result.current.progress?.percent).toBe(100)
    expect(generate).toHaveBeenCalledTimes(2)
    unmount()
    expect(worker.dispose).toHaveBeenCalledOnce()
    generate.mockRestore()
  })

  it.each([new DOMException('Cancelled', 'AbortError'), 'failure'])(
    'handles aborted and non-Error generation failures (%s)',
    async (failure) => {
      worker.load.mockRejectedValue(failure)
      const { result } = renderHook(useAi, { wrapper: AiProvider })
      await waitFor(() => expect(result.current.available).toBe(true))
      await act(async () => {
        await expect(result.current.generateFilterPlan('', [])).rejects.toBe(failure)
      })
      expect(result.current.runtimeStatus).toBe(typeof failure === 'string' ? 'error' : 'idle')
      if (typeof failure === 'string')
        await expect(result.current.generateFilterPlan('', [])).rejects.toThrow('unavailable')
      else {
        expect(result.current.available).toBe(true)
        expect(result.current.progress).toBeNull()
      }
    },
  )

  it('disables AI for the browser session after model loading fails and allows retry', async () => {
    worker.load.mockRejectedValue(new Error('ONNX initialization failed'))
    render(
      <AiProvider>
        <RuntimeProbe />
      </AiProvider>,
    )

    await waitFor(() => expect(screen.getByRole('button', { name: 'Generate' })).toBeEnabled())
    fireEvent.click(screen.getByRole('button', { name: 'Generate' }))

    await waitFor(() => expect(screen.getByTestId('availability')).toHaveTextContent('runtime-error'))
    expect(screen.getByTestId('runtime-error')).toHaveTextContent('ONNX initialization failed')
    expect(screen.getByRole('button', { name: 'Generate' })).toBeDisabled()

    fireEvent.click(screen.getByRole('button', { name: 'Retry' }))
    expect(screen.getByRole('button', { name: 'Generate' })).toBeEnabled()
    expect(worker.dispose).toHaveBeenCalledOnce()
  })
})

function RuntimeProbe() {
  const ai = useAi()
  return (
    <>
      <span data-testid="availability">{ai.available ? 'available' : ai.reason}</span>
      <span data-testid="runtime-error">{ai.error}</span>
      <button
        type="button"
        disabled={!ai.available}
        onClick={() => void ai.generateFilterPlan('find tables', []).catch(() => undefined)}
      >
        Generate
      </button>
      <button type="button" onClick={ai.retry}>
        Retry
      </button>
    </>
  )
}
