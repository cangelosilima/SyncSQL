import { afterEach, expect, it, vi } from 'vitest'
import { WorkerEmbeddingAdapter } from './WorkerEmbeddingAdapter'

class FakeWorker extends EventTarget {
  static latest: FakeWorker
  postMessage = vi.fn()
  terminate = vi.fn()
  constructor() {
    super()
    FakeWorker.latest = this
  }
  reply(data: Record<string, unknown>) {
    this.dispatchEvent(new MessageEvent('message', { data }))
  }
}
vi.stubGlobal('Worker', FakeWorker)
afterEach(() => vi.clearAllMocks())

it('coalesces loads, reports progress, embeds and removes worker listeners on disposal', async () => {
  const adapter = new WorkerEmbeddingAdapter()
  const worker = FakeWorker.latest
  await expect(adapter.embed(['hello'])).rejects.toThrow('not been loaded')
  const onProgress = vi.fn()
  const controller = new AbortController()
  const load = adapter.load({ modelBaseUrl: '/models', onProgress, signal: controller.signal })
  expect(adapter.load({ modelBaseUrl: '/models' })).toBe(load)
  worker.reply({ id: 999, type: 'loaded' })
  worker.reply({ id: 1, type: 'progress' })
  worker.reply({ id: 1, type: 'progress', percent: 50, file: 'model', status: 'download' })
  expect(onProgress.mock.calls).toEqual([
    [{ percent: null, file: null, status: 'loading' }],
    [{ percent: 50, file: 'model', status: 'download' }],
  ])
  worker.reply({ id: 1, type: 'loaded' })
  await load
  await adapter.load({ modelBaseUrl: '/models' })
  expect(worker.postMessage).toHaveBeenCalledTimes(1)
  const embedding = adapter.embed(['hello'])
  worker.reply({ id: 2, type: 'progress' })
  worker.reply({ id: 2, type: 'embeddings', embeddings: [[1, 2]] })
  await expect(embedding).resolves.toEqual([[1, 2]])
  const empty = adapter.embed([])
  worker.reply({ id: 3, type: 'embeddings' })
  await expect(empty).resolves.toEqual([])
  adapter.dispose()
  expect(worker.terminate).toHaveBeenCalledOnce()
})

it.each([undefined, 'Bad model'])('rejects response errors (%s) and permits retry', async (message) => {
  const adapter = new WorkerEmbeddingAdapter()
  const load = adapter.load({ modelBaseUrl: '/' })
  FakeWorker.latest.reply({ id: 1, type: 'error', message })
  await expect(load).rejects.toThrow(message ?? 'The AI worker failed.')
  const retry = adapter.load({ modelBaseUrl: '/' })
  FakeWorker.latest.reply({ id: 2, type: 'loaded' })
  await retry
  adapter.dispose()
})

it('honors already aborted and subsequently aborted requests, ignoring late responses', async () => {
  const adapter = new WorkerEmbeddingAdapter()
  const controller = new AbortController()
  controller.abort()
  await expect(adapter.load({ modelBaseUrl: '/', signal: controller.signal })).rejects.toMatchObject({
    name: 'AbortError',
  })
  expect(FakeWorker.latest.postMessage).not.toHaveBeenCalled()
  const active = new AbortController()
  const load = adapter.load({ modelBaseUrl: '/', signal: active.signal })
  active.abort()
  await expect(load).rejects.toMatchObject({ name: 'AbortError' })
  FakeWorker.latest.reply({ id: 1, type: 'loaded' })
  await expect(adapter.embed([])).rejects.toThrow('not been loaded')
  adapter.dispose()
})

it.each(['crash', ''])('rejects pending requests on worker failure (%s)', async (message) => {
  const adapter = new WorkerEmbeddingAdapter()
  const load = adapter.load({ modelBaseUrl: '/' })
  FakeWorker.latest.dispatchEvent(new ErrorEvent('error', { message }))
  await expect(load).rejects.toThrow(message || 'The AI worker failed.')
  adapter.dispose()
})

it('rejects outstanding requests when disposed', async () => {
  const adapter = new WorkerEmbeddingAdapter()
  const load = adapter.load({ modelBaseUrl: '/' })
  adapter.dispose()
  await expect(load).rejects.toThrow('stopped')
})
