import type { CatalogNode } from '../types'
import type { FilterTokenInput } from '../lib/filters'

export type AiAvailabilityReason = 'model-missing' | 'lfs-unresolved' | 'checksum-mismatch' | 'packaging-failed' | 'manifest-unavailable' | 'runtime-error'

export interface AiCapability<TId extends string = string> {
  id: TId
  available: boolean
  version: number
  model?: string
  reason?: AiAvailabilityReason
}

export type FilterGeneratorCapability = Omit<AiCapability<'filter.generate'>, 'id'>

export interface AiCapabilityManifest {
  filterGenerator: FilterGeneratorCapability
}

export interface ModelLoadProgress {
  percent: number | null
  file: string | null
  status: string
}

export interface EmbeddingLoadOptions {
  modelBaseUrl: string
  signal?: AbortSignal
  onProgress?: (progress: ModelLoadProgress) => void
}

export interface EmbeddingAdapter {
  load(options: EmbeddingLoadOptions): Promise<void>
  embed(texts: string[], signal?: AbortSignal): Promise<number[][]>
  dispose(): void
}

export interface FilterPlanV1 {
  version: 1
  tokens: FilterTokenInput[]
  contentQuery: string
  confidence: 'high' | 'medium' | 'low'
  warnings: string[]
  unsupportedFragments: string[]
}

export interface FilterPlanner {
  generate(query: string, nodes: CatalogNode[], adapter: EmbeddingAdapter, signal?: AbortSignal): Promise<FilterPlanV1>
}
