import { expect, it, vi } from 'vitest'
import { makeNode } from '../test/fixtures'
it('returns filtered object IDs from worker requests', async () => {
  const worker = { onmessage: null as unknown as (event: MessageEvent) => void, postMessage: vi.fn() }
  vi.stubGlobal('self', worker)
  try {
    await import('./catalogSearch.worker')
    worker.onmessage(
      new MessageEvent('message', {
        data: {
          nodes: [makeNode({ id: 'orders' }), makeNode({ id: 'users' })],
          tokens: [{ id: 'query', attribute: 'name', operator: 'contains', values: ['orders'] }],
        },
      }),
    )
    expect(worker.postMessage).toHaveBeenCalledWith(['orders'])
  } finally {
    vi.unstubAllGlobals()
  }
})
