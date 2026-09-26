import { afterEach, beforeEach, expect, it, vi } from 'vitest'

const { env, pipeline } = vi.hoisted(() => ({ env: {} as Record<string, unknown>, pipeline: vi.fn() }))
vi.mock('@huggingface/transformers', () => ({ env, pipeline }))
let receive: (event: { data: Record<string, unknown> }) => void
const postMessage = vi.fn()
beforeEach(async () => {
  vi.resetModules()
  pipeline.mockReset()
  postMessage.mockClear()
  vi.stubGlobal('self', {
    addEventListener: (_: string, handler: typeof receive) => {
      receive = handler
    },
    postMessage,
  })
  vi.stubGlobal('navigator', {})
  await import('./embedding.worker')
})
afterEach(() => vi.unstubAllGlobals())
async function send(data: Record<string, unknown>) {
  receive({ data })
  await vi.waitFor(() =>
    expect(postMessage).toHaveBeenCalledWith(
      expect.objectContaining({ id: data.id, type: expect.stringMatching(/^(loaded|embeddings|error)$/) }),
    ),
  )
  return postMessage.mock.calls.findLast(([value]) => value.id === data.id)![0]
}

it('validates required input and loading before embedding', async () => {
  expect(await send({ id: 1, type: 'load' })).toMatchObject({
    type: 'error',
    message: 'The local model base URL is missing.',
  })
  expect(await send({ id: 2, type: 'embed' })).toMatchObject({ type: 'error', message: 'Embedding input is missing.' })
  expect(await send({ id: 3, type: 'embed', texts: [] })).toMatchObject({
    type: 'error',
    message: 'The embedding model has not been loaded.',
  })
})

it('loads once with WASM, shares concurrent loads, normalizes progress, and returns embeddings', async () => {
  let resolve!: (extractor: unknown) => void
  pipeline.mockImplementation((_task, _model, options) => {
    for (const progress of [null, 'bad', {}, { progress: -5, file: 'weights', status: 'download' }, { progress: 150 }])
      options.progress_callback(progress)
    return new Promise((done) => {
      resolve = done
    })
  })
  receive({ data: { id: 1, type: 'load', modelBaseUrl: '/models' } })
  receive({ data: { id: 2, type: 'load', modelBaseUrl: '/models' } })
  const extractor = vi.fn().mockResolvedValue({ tolist: () => [[1, 2]] })
  resolve(extractor)
  await vi.waitFor(() => expect(postMessage).toHaveBeenCalledWith({ id: 2, type: 'loaded' }))
  expect(env).toMatchObject({
    localModelPath: '/models/',
    allowRemoteModels: false,
    allowLocalModels: true,
    useBrowserCache: true,
  })
  expect(postMessage).toHaveBeenCalledWith({ id: 1, type: 'progress', percent: 0, file: 'weights', status: 'download' })
  expect(postMessage).toHaveBeenCalledWith({ id: 1, type: 'progress', percent: 100, file: null, status: 'loading' })
  await send({ id: 3, type: 'load', modelBaseUrl: '/' })
  expect(pipeline).toHaveBeenCalledOnce()
  expect(await send({ id: 4, type: 'embed', texts: ['hi'] })).toEqual({
    id: 4,
    type: 'embeddings',
    embeddings: [[1, 2]],
  })
  expect(extractor).toHaveBeenCalledWith(['hi'], { pooling: 'mean', normalize: true })
  for (const value of [null, [1], [['bad']]]) {
    extractor.mockResolvedValueOnce({ tolist: () => value })
    postMessage.mockClear()
    expect(await send({ id: 5, type: 'embed', texts: ['hi'] })).toMatchObject({
      type: 'error',
      message: expect.stringContaining('tensor shape'),
    })
  }
})

it('uses WebGPU when available', async () => {
  vi.stubGlobal('navigator', { gpu: {} })
  pipeline.mockResolvedValue(vi.fn())
  expect(await send({ id: 1, type: 'load', modelBaseUrl: '/models/' })).toEqual({ id: 1, type: 'loaded' })
  expect(pipeline).toHaveBeenCalledWith(
    'feature-extraction',
    'all-MiniLM-L6-v2',
    expect.objectContaining({ device: 'webgpu' }),
  )
  expect(env.localModelPath).toBe('/models/')
})

it.each([new Error('GPU failed'), 'GPU failed'])('falls back to WASM after a GPU failure (%s)', async (error) => {
  vi.stubGlobal('navigator', { gpu: {} })
  pipeline.mockRejectedValueOnce(error).mockResolvedValueOnce(vi.fn())
  await send({ id: 1, type: 'load', modelBaseUrl: '/' })
  expect(postMessage).toHaveBeenCalledWith(
    expect.objectContaining({ type: 'progress', status: 'WebGPU unavailable; using WASM (GPU failed)' }),
  )
  expect(pipeline).toHaveBeenLastCalledWith(
    'feature-extraction',
    'all-MiniLM-L6-v2',
    expect.objectContaining({ device: 'wasm' }),
  )
})

it('reports non-Error failures and retries without navigator', async () => {
  vi.stubGlobal('navigator', undefined)
  pipeline.mockRejectedValueOnce('offline').mockResolvedValueOnce(vi.fn())
  expect(await send({ id: 1, type: 'load', modelBaseUrl: '/' })).toMatchObject({ type: 'error', message: 'offline' })
  expect(await send({ id: 2, type: 'load', modelBaseUrl: '/' })).toMatchObject({ type: 'loaded' })
})
