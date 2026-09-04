import type { EmbeddingAdapter, EmbeddingLoadOptions, ModelLoadProgress } from './types'

interface WorkerResponse {
  id: number
  type: 'loaded' | 'embeddings' | 'progress' | 'error'
  embeddings?: number[][]
  message?: string
  percent?: number | null
  file?: string | null
  status?: string
}

interface PendingRequest<T> {
  resolve: (value: T) => void
  reject: (reason: Error) => void
  onProgress?: (progress: ModelLoadProgress) => void
  cleanup?: () => void
}

export class WorkerEmbeddingAdapter implements EmbeddingAdapter {
  private readonly worker = new Worker(new URL('./embedding.worker.ts', import.meta.url), { type: 'module' })
  private readonly pending = new Map<number, PendingRequest<unknown>>()
  private sequence = 0
  private loaded = false
  private loadPromise: Promise<void> | null = null

  constructor() {
    this.worker.addEventListener('message', this.handleMessage)
    this.worker.addEventListener('error', this.handleWorkerError)
  }

  load(options: EmbeddingLoadOptions): Promise<void> {
    if (this.loaded) return Promise.resolve()
    if (this.loadPromise) return this.loadPromise
    this.loadPromise = this.request<void>('load', { modelBaseUrl: options.modelBaseUrl }, options.signal, options.onProgress)
      .then(() => {
        this.loaded = true
      })
      .finally(() => {
        this.loadPromise = null
      })
    return this.loadPromise
  }

  embed(texts: string[], signal?: AbortSignal): Promise<number[][]> {
    if (!this.loaded) return Promise.reject(new Error('The embedding model has not been loaded.'))
    return this.request<number[][]>('embed', { texts }, signal)
  }

  dispose() {
    this.worker.removeEventListener('message', this.handleMessage)
    this.worker.removeEventListener('error', this.handleWorkerError)
    this.worker.terminate()
    for (const request of this.pending.values()) {
      request.cleanup?.()
      request.reject(new Error('The AI worker was stopped.'))
    }
    this.pending.clear()
  }

  private request<T>(type: 'load' | 'embed', payload: Record<string, unknown>, signal?: AbortSignal, onProgress?: (progress: ModelLoadProgress) => void): Promise<T> {
    if (signal?.aborted) return Promise.reject(abortError())
    const id = ++this.sequence
    return new Promise<T>((resolve, reject) => {
      const abort = () => {
        this.pending.delete(id)
        reject(abortError())
      }
      signal?.addEventListener('abort', abort, { once: true })
      this.pending.set(id, {
        resolve: resolve as (value: unknown) => void,
        reject,
        onProgress,
        cleanup: () => signal?.removeEventListener('abort', abort),
      })
      this.worker.postMessage({ id, type, ...payload })
    })
  }

  private readonly handleMessage = (event: MessageEvent<WorkerResponse>) => {
    const response = event.data
    const request = this.pending.get(response.id)
    if (!request) return
    if (response.type === 'progress') {
      request.onProgress?.({
        percent: response.percent ?? null,
        file: response.file ?? null,
        status: response.status ?? 'loading',
      })
      return
    }

    this.pending.delete(response.id)
    request.cleanup?.()
    if (response.type === 'error') {
      request.reject(new Error(response.message ?? 'The AI worker failed.'))
    } else if (response.type === 'embeddings') {
      request.resolve(response.embeddings ?? [])
    } else {
      request.resolve(undefined)
    }
  }

  private readonly handleWorkerError = (event: ErrorEvent) => {
    const error = new Error(event.message || 'The AI worker failed.')
    for (const request of this.pending.values()) {
      request.cleanup?.()
      request.reject(error)
    }
    this.pending.clear()
  }
}

function abortError(): DOMException {
  return new DOMException('The operation was aborted.', 'AbortError')
}
