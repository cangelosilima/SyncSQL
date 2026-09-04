import { env, pipeline } from '@huggingface/transformers'

const MODEL_ID = 'all-MiniLM-L6-v2'

interface WorkerRequest {
  id: number
  type: 'load' | 'embed'
  modelBaseUrl?: string
  texts?: string[]
}

interface TensorLike {
  tolist(): unknown
}

type Extractor = (texts: string[], options: { pooling: 'mean'; normalize: true }) => Promise<TensorLike>

let extractor: Extractor | null = null
let loading: Promise<void> | null = null

env.allowRemoteModels = false
env.allowLocalModels = true
env.useBrowserCache = true

self.addEventListener('message', (event: MessageEvent<WorkerRequest>) => {
  void handleRequest(event.data)
})

async function handleRequest(request: WorkerRequest) {
  try {
    if (request.type === 'load') {
      if (!request.modelBaseUrl) throw new Error('The local model base URL is missing.')
      await loadModel(request.id, request.modelBaseUrl)
      post({ type: 'loaded', id: request.id })
      return
    }

    if (!request.texts) throw new Error('Embedding input is missing.')
    if (!extractor) throw new Error('The embedding model has not been loaded.')
    const output = await extractor(request.texts, { pooling: 'mean', normalize: true })
    const embeddings = output.tolist()
    if (!isNumberMatrix(embeddings)) throw new Error('The embedding model returned an unexpected tensor shape.')
    post({ type: 'embeddings', id: request.id, embeddings })
  } catch (error) {
    post({ type: 'error', id: request.id, message: error instanceof Error ? error.message : String(error) })
  }
}

async function loadModel(requestId: number, modelBaseUrl: string) {
  if (extractor) return
  if (loading) return loading

  env.localModelPath = ensureTrailingSlash(modelBaseUrl)
  loading = (async () => {
    const progressCallback = (progress: unknown) => postProgress(requestId, progress)
    const hasWebGpu = typeof navigator !== 'undefined' && 'gpu' in navigator
    if (hasWebGpu) {
      try {
        const created = await pipeline('feature-extraction', MODEL_ID, {
          dtype: 'q8',
          device: 'webgpu',
          progress_callback: progressCallback,
        })
        extractor = created as unknown as Extractor
        return
      } catch (error) {
        post({
          type: 'progress',
          id: requestId,
          percent: null,
          file: null,
          status: `WebGPU unavailable; using WASM (${error instanceof Error ? error.message : String(error)})`,
        })
      }
    }

    const created = await pipeline('feature-extraction', MODEL_ID, {
      dtype: 'q8',
      device: 'wasm',
      progress_callback: progressCallback,
    })
    extractor = created as unknown as Extractor
  })().finally(() => {
    loading = null
  })

  return loading
}

function postProgress(id: number, value: unknown) {
  if (!value || typeof value !== 'object') return
  const progress = value as Record<string, unknown>
  const rawPercent = typeof progress.progress === 'number' ? progress.progress : null
  const percent = rawPercent === null ? null : Math.max(0, Math.min(100, rawPercent))
  post({
    type: 'progress',
    id,
    percent,
    file: typeof progress.file === 'string' ? progress.file : null,
    status: typeof progress.status === 'string' ? progress.status : 'loading',
  })
}

function post(message: Record<string, unknown>) {
  self.postMessage(message)
}

function ensureTrailingSlash(value: string): string {
  return value.endsWith('/') ? value : `${value}/`
}

function isNumberMatrix(value: unknown): value is number[][] {
  return Array.isArray(value) && value.every((row) => Array.isArray(row) && row.every((item) => typeof item === 'number'))
}
