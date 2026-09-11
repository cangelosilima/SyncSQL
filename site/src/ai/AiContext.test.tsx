import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { AiProvider, parseCapabilityManifest, useAi } from './AiContext'

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
