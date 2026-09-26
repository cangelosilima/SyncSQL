import { act, renderHook, waitFor } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { buildIndex } from './catalog'
import type { PartitionedCatalog } from './partitionedCatalog'
import { useCatalogFilter, useCatalogSelection } from './useCatalogData'
import { makeCatalog, makeNode } from '../test/fixtures'
import type { CatalogNode } from '../types'
import type { FilterToken } from './filters'

function deferred<T>() {
  let resolve!: (value: T) => void
  let reject!: (reason: Error) => void
  const promise = new Promise<T>((yes, no) => {
    resolve = yes
    reject = no
  })
  return { promise, resolve, reject }
}

describe('partition loading states', () => {
  it.each([undefined, 'a'])('loads permissions only for the selected neighborhood (focus=%s)', async (focus) => {
    const index = buildIndex(makeCatalog({ nodes: [makeNode({ id: 'a' }), makeNode({ id: 'b' })] }))
    const permissions = vi.fn().mockResolvedValue(
      new Map([
        ['a', [{ grantee: 'reader' }]],
        ['missing', []],
      ]),
    )
    const neighborhood = vi.fn().mockResolvedValue(index)
    index.source = { edges: async () => index, neighborhood, permissions } as unknown as PartitionedCatalog
    const { result } = renderHook(() => useCatalogSelection(index, { focus, grantIds: ['a', 'b'] }))
    await waitFor(() => expect(result.current.loading).toBe(false))
    expect(permissions.mock.calls[0][0]).toEqual(focus ? ['a'] : ['a', 'b'])
    expect(result.current.index?.byId.get('a')?.grants).toEqual([{ grantee: 'reader' }])
  })

  it.each([new Error('offline'), 'offline'])('reports graph and search failures (%s)', async (failure) => {
    const index = buildIndex(makeCatalog())
    index.source = {
      alerts: async () => {
        throw failure
      },
      search: async () => {
        throw failure
      },
    } as unknown as PartitionedCatalog
    const selection = renderHook(() => useCatalogSelection(index, { alerts: true }))
    const filter = renderHook(() =>
      useCatalogFilter(index, [{ id: 'q', attribute: null, operator: 'contains', values: ['x'] }]),
    )
    await waitFor(() => expect(selection.result.current.error).toBe('offline'))
    await waitFor(() => expect(filter.result.current.error).toBe('offline'))
  })

  it('returns local filters without a source and publishes successful remote search matches', async () => {
    const empty = renderHook(() => useCatalogFilter(null, []))
    expect(empty.result.current.nodes).toEqual([])
    const index = buildIndex(makeCatalog({ nodes: [makeNode({ id: 'a' })] }))
    index.source = { search: async () => index.catalog.nodes } as unknown as PartitionedCatalog
    const remote = renderHook(() =>
      useCatalogFilter(index, [{ id: 'q', attribute: 'ddl', operator: 'contains', values: ['a'] }]),
    )
    await waitFor(() => expect(remote.result.current.nodes).toEqual(index.catalog.nodes))
  })

  it('ignores pending permission results and rejected operations after unmount', async () => {
    const index = buildIndex(makeCatalog())
    const permission = deferred<Map<string, never[]>>()
    const search = deferred<CatalogNode[]>()
    index.source = {
      edges: async () => index,
      permissions: () => permission.promise,
      search: () => search.promise,
    } as unknown as PartitionedCatalog
    const selection = renderHook(() => useCatalogSelection(index, { grantIds: ['a'] }))
    const filter = renderHook(() =>
      useCatalogFilter(index, [{ id: 'q', attribute: null, operator: 'contains', values: ['a'] }]),
    )
    await act(async () => {})
    selection.unmount()
    filter.unmount()
    await act(async () => {
      permission.resolve(new Map())
      search.reject(new Error('cancelled'))
    })
    const graph = deferred<ReturnType<typeof buildIndex>>()
    index.source = { edges: () => graph.promise } as unknown as PartitionedCatalog
    const cancelled = renderHook(() => useCatalogSelection(index, {}))
    cancelled.unmount()
    await act(async () => graph.reject(new Error('cancelled')))
    expect(cancelled.result.current.error).toBeUndefined()
  })
  it('does not display a previous object after navigating while details are loading', async () => {
    const first = makeNode({ id: 'first' })
    const second = makeNode({ id: 'second' })
    const index = buildIndex(makeCatalog({ nodes: [first, second] }))
    const requests = { first: deferred<CatalogNode>(), second: deferred<CatalogNode>() }
    index.source = {
      edges: async () => ({ ...index }),
      object: (id: 'first' | 'second') => requests[id].promise,
    } as unknown as PartitionedCatalog
    const { result, rerender } = renderHook(({ id }) => useCatalogSelection(index, { objectId: id }), {
      initialProps: { id: 'first' },
    })
    expect(result.current.loading).toBe(true)
    rerender({ id: 'second' })
    await act(async () => requests.second.resolve({ ...second, ddl: 'second detail' }))
    await waitFor(() => expect(result.current.loading).toBe(false))
    expect(result.current.index?.byId.get('second')?.ddl).toBe('second detail')
    await act(async () => requests.first.resolve({ ...first, ddl: 'stale detail' }))
    expect(result.current.index?.byId.get('first')?.ddl).toBe(first.ddl)
    expect(index.byId.get('second')?.ddl).toBe(second.ddl)
  })

  it('keeps failed loads explicit and cancels obsolete text searches', async () => {
    const index = buildIndex(makeCatalog({ nodes: [makeNode({ id: 'match' })] }))
    const old = deferred<CatalogNode[]>()
    const current = deferred<CatalogNode[]>()
    let oldSignal: AbortSignal | undefined
    index.source = {
      search: (_nodes: CatalogNode[], tokens: FilterToken[], signal: AbortSignal) => {
        if (tokens[0].values[0] === 'old') {
          oldSignal = signal
          return old.promise
        }
        return current.promise
      },
    } as unknown as PartitionedCatalog
    const token = (value: string): FilterToken[] => [
      { id: 'query', attribute: 'ddl', operator: 'contains', values: [value] },
    ]
    const { result, rerender } = renderHook(({ value }) => useCatalogFilter(index, token(value)), {
      initialProps: { value: 'old' },
    })
    rerender({ value: 'new' })
    expect(oldSignal?.aborted).toBe(true)
    await act(async () => current.reject(new Error('Partition unavailable')))
    await waitFor(() => expect(result.current.error).toBe('Partition unavailable'))
    await act(async () => old.resolve(index.catalog.nodes))
    expect(result.current.nodes).toEqual([])
    expect(result.current.error).toBe('Partition unavailable')
  })
})
