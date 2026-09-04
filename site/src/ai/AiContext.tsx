import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState, type ReactNode } from 'react'
import type { CatalogNode } from '../types'
import { EmbeddingFilterPlanner } from './filterPlanner'
import type { AiAvailabilityReason, AiCapabilityManifest, FilterPlanV1, ModelLoadProgress } from './types'
import { WorkerEmbeddingAdapter } from './WorkerEmbeddingAdapter'

type AiRuntimeStatus = 'idle' | 'loading' | 'running' | 'error'

interface AiContextValue {
  checking: boolean
  available: boolean
  reason: AiAvailabilityReason | null
  runtimeStatus: AiRuntimeStatus
  progress: ModelLoadProgress | null
  error: string | null
  generateFilterPlan(query: string, nodes: CatalogNode[], signal?: AbortSignal): Promise<FilterPlanV1>
  retry(): void
}

const defaultValue: AiContextValue = {
  checking: true,
  available: false,
  reason: null,
  runtimeStatus: 'idle',
  progress: null,
  error: null,
  generateFilterPlan: () => Promise.reject(new Error('AI is not ready.')),
  retry: () => undefined,
}

const AiContext = createContext<AiContextValue>(defaultValue)

export function AiProvider({ children }: { children: ReactNode }) {
  const [checking, setChecking] = useState(true)
  const [deploymentCapability, setDeploymentCapability] = useState<AiCapabilityManifest['filterGenerator'] | null>(null)
  const [sessionReason, setSessionReason] = useState<AiAvailabilityReason | null>(null)
  const [runtimeStatus, setRuntimeStatus] = useState<AiRuntimeStatus>('idle')
  const [progress, setProgress] = useState<ModelLoadProgress | null>(null)
  const [error, setError] = useState<string | null>(null)
  const adapterRef = useRef<WorkerEmbeddingAdapter | null>(null)
  const plannerRef = useRef(new EmbeddingFilterPlanner())

  useEffect(() => {
    const controller = new AbortController()
    const manifestUrl = new URL('data/ai-capabilities.json', document.baseURI)
    fetch(manifestUrl, { signal: controller.signal })
      .then(async (response) => {
        if (!response.ok) throw new Error(`Capability manifest returned ${response.status}.`)
        const value = await response.json() as unknown
        setDeploymentCapability(parseCapabilityManifest(value).filterGenerator)
      })
      .catch((manifestError: unknown) => {
        if (!controller.signal.aborted) {
          setDeploymentCapability({ available: false, version: 1, reason: 'manifest-unavailable' })
          setError(manifestError instanceof Error ? manifestError.message : String(manifestError))
        }
      })
      .finally(() => {
        if (!controller.signal.aborted) setChecking(false)
      })
    return () => controller.abort()
  }, [])

  useEffect(() => () => adapterRef.current?.dispose(), [])

  const available = Boolean(deploymentCapability?.available) && sessionReason === null
  const reason = sessionReason ?? deploymentCapability?.reason ?? null

  const generateFilterPlan = useCallback(async (query: string, nodes: CatalogNode[], signal?: AbortSignal) => {
    if (!deploymentCapability?.available || sessionReason) throw new Error('AI is unavailable in this deployment.')
    try {
      setError(null)
      setRuntimeStatus('loading')
      const adapter = adapterRef.current ?? new WorkerEmbeddingAdapter()
      adapterRef.current = adapter
      const modelBaseUrl = new URL('models/', document.baseURI).href
      await adapter.load({ modelBaseUrl, signal, onProgress: setProgress })
      setRuntimeStatus('running')
      const plan = await plannerRef.current.generate(query, nodes, adapter, signal)
      setRuntimeStatus('idle')
      return plan
    } catch (generationError) {
      if (generationError instanceof DOMException && generationError.name === 'AbortError') {
        adapterRef.current?.dispose()
        adapterRef.current = null
        setRuntimeStatus('idle')
        setProgress(null)
        throw generationError
      }
      const message = generationError instanceof Error ? generationError.message : String(generationError)
      setError(message)
      setRuntimeStatus('error')
      setSessionReason('runtime-error')
      throw generationError
    }
  }, [deploymentCapability, sessionReason])

  const retry = useCallback(() => {
    adapterRef.current?.dispose()
    adapterRef.current = null
    setSessionReason(null)
    setRuntimeStatus('idle')
    setProgress(null)
    setError(null)
  }, [])

  const value = useMemo<AiContextValue>(() => ({
    checking,
    available,
    reason,
    runtimeStatus,
    progress,
    error,
    generateFilterPlan,
    retry,
  }), [available, checking, error, generateFilterPlan, progress, reason, retry, runtimeStatus])

  return <AiContext.Provider value={value}>{children}</AiContext.Provider>
}

export function useAi(): AiContextValue {
  return useContext(AiContext)
}

export function parseCapabilityManifest(value: unknown): AiCapabilityManifest {
  if (!value || typeof value !== 'object') return unavailableManifest()
  const raw = (value as Record<string, unknown>).filterGenerator
  if (!raw || typeof raw !== 'object') return unavailableManifest()
  const capability = raw as Record<string, unknown>
  if (capability.available !== true || typeof capability.model !== 'string' || capability.version !== 1) {
    const reason = isAvailabilityReason(capability.reason) ? capability.reason : 'manifest-unavailable'
    return { filterGenerator: { available: false, version: 1, reason } }
  }
  return { filterGenerator: { available: true, version: 1, model: capability.model } }
}

function unavailableManifest(): AiCapabilityManifest {
  return { filterGenerator: { available: false, version: 1, reason: 'manifest-unavailable' } }
}

function isAvailabilityReason(value: unknown): value is AiAvailabilityReason {
  return value === 'model-missing'
    || value === 'lfs-unresolved'
    || value === 'checksum-mismatch'
    || value === 'packaging-failed'
    || value === 'manifest-unavailable'
    || value === 'runtime-error'
}
