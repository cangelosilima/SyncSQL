import { useEffect, useMemo, useState } from 'react'
import type { CatalogIndex } from './catalog'
import type { CatalogNode } from '../types'
import { applyFilters, type FilterToken } from './filters'
import { getDirectionalNeighborhood } from './neighborhood'

interface Selection {
  objectId?: string
  focus?: string
  dependencies?: number
  dependents?: number
  ids?: string[]
  alerts?: boolean
  grantIds?: string[]
}

/** Gate views until all required partitions have loaded; never show partial graphs as complete. */
export function useCatalogSelection(base: CatalogIndex | null, selection: Selection) {
  const key = JSON.stringify(selection)
  const [result, setResult] = useState<{ base: CatalogIndex; key: string; index?: CatalogIndex; error?: string }>()
  useEffect(() => {
    if (!base?.source) return
    const source = base.source
    const request = JSON.parse(key) as Selection
    const controller = new AbortController()
    const load = async () => {
      const graph = request.focus
        ? source.neighborhood(request.focus, request.dependencies ?? 1, request.dependents ?? 1, controller.signal)
        : request.alerts
          ? source.alerts(controller.signal)
          : source.edges(request.ids ?? [], controller.signal)
      const [index, node] = await Promise.all([graph, request.objectId ? source.object(request.objectId) : undefined])
      if (controller.signal.aborted) return
      if (request.grantIds?.length) {
        const neighborhood = request.focus
          ? new Set(
              getDirectionalNeighborhood(index, request.focus, request.dependencies ?? 1, request.dependents ?? 1),
            )
          : undefined
        const ids = neighborhood ? request.grantIds.filter((id) => neighborhood.has(id)) : request.grantIds
        const grants = await source.permissions(ids, controller.signal)
        index.byId = new Map(index.byId)
        for (const [id, permissions] of grants) {
          const summary = index.byId.get(id)
          if (summary) index.byId.set(id, { ...summary, grants: permissions })
        }
      }
      if (controller.signal.aborted) return
      if (node) {
        index.byId = new Map(index.byId)
        index.byId.set(node.id, node)
      }
      setResult({ base, key, index })
    }
    void load().catch((error: unknown) => {
      if (!controller.signal.aborted)
        setResult({ base, key, error: error instanceof Error ? error.message : String(error) })
    })
    return () => controller.abort()
  }, [base, key])
  const current = result?.base === base && result.key === key ? result : undefined
  return {
    index: current?.index ?? base,
    loading: Boolean(base?.source && !current),
    error: current?.error,
  }
}

export function useCatalogFilter(index: CatalogIndex | null, tokens: FilterToken[]) {
  const nodes = index?.catalog.nodes
  const source = index?.source
  const key = JSON.stringify(tokens)
  const needsSearch = Boolean(source && tokens.some((token) => token.attribute === null || token.attribute === 'ddl'))
  const [result, setResult] = useState<{ key: string; source: typeof source; nodes?: CatalogNode[]; error?: string }>()
  const filtered = useMemo(() => (needsSearch ? [] : applyFilters(nodes ?? [], tokens)), [nodes, tokens, needsSearch])
  useEffect(() => {
    if (!source || !needsSearch || !nodes) return
    const controller = new AbortController()
    void source
      .search(nodes, JSON.parse(key) as FilterToken[], controller.signal)
      .then((matches) => {
        if (!controller.signal.aborted) setResult({ key, source, nodes: matches })
      })
      .catch((error: unknown) => {
        if (!controller.signal.aborted)
          setResult({ key, source, error: error instanceof Error ? error.message : String(error) })
      })
    return () => controller.abort()
  }, [source, nodes, needsSearch, key])
  const current = result && result.source === source && result.key === key ? result : undefined
  return {
    nodes: needsSearch ? (current?.nodes ?? []) : filtered,
    loading: needsSearch && !current,
    error: needsSearch ? current?.error : undefined,
  }
}
