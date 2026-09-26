import { expect, it, vi } from 'vitest'
import { EmbeddingFilterPlanner } from './filterPlanner'
import { makeNode } from '../test/fixtures'
import type { EmbeddingAdapter } from './types'
const planner = new EmbeddingFilterPlanner()

it('recognizes positive descriptions, semantic negation and absent type aliases', async () => {
  expect((await planner.generate('description "invoices"', [], adapter(5))).tokens[0]).toMatchObject({
    attribute: 'description',
    operator: 'contains',
  })
  expect((await planner.generate('not unusual "Orders"', [], adapter(4))).tokens[0]).toMatchObject({
    attribute: 'name',
    operator: 'is-not',
  })
  expect((await planner.generate('not please invoices', [], adapter(4))).tokens[0].values).toEqual(['not invoices'])
  expect((await planner.generate('functions', [makeNode({ id: 'table' })], adapter(0))).confidence).toBe('low')
})
function adapter(intent: number, score = 1, tie = false): EmbeddingAdapter {
  return {
    load: vi.fn(),
    dispose: vi.fn(),
    embed: vi.fn(async () => [[1], ...Array.from({ length: 7 }, (_, i) => [tie || i === intent ? score : 0])]),
  }
}
it.each([
  [4, 'unusual "Orders"', 'name'],
  [5, 'unusual "Orders"', 'description'],
  [6, 'unusual "Orders"', 'content'],
  [4, 'please find object name Orders', 'name'],
  [5, 'please find documented invoices', 'description'],
  [6, 'please find code invoices', 'content'],
] as const)('classifies semantic intent %s for %s', async (intent, query, attribute) => {
  const plan = await planner.generate(query, [], adapter(intent))
  expect(plan.confidence).toBe('medium')
  if (attribute === 'content') expect(plan.contentQuery).toBeTruthy()
  else expect(plan.tokens[0]).toMatchObject({ attribute, operator: 'contains' })
})
it.each([0, 1, 2, 3])('rejects unresolved semantic facet %s', async (intent) => {
  const plan = await planner.generate('unrecognized value', [], adapter(intent))
  expect(plan.confidence).toBe('low')
  expect(plan.warnings[0]).toContain('could not resolve')
})
it('rejects uncertain, tied, missing and incomplete vectors and empty semantic literals', async () => {
  for (const embedding of [
    adapter(4, 0.2),
    adapter(4, 1, true),
    { ...adapter(4), embed: vi.fn(async () => []) },
    { ...adapter(4), embed: vi.fn(async () => [[1]]) },
  ]) {
    expect((await planner.generate('unrecognized', [], embedding)).confidence).toBe('low')
  }
  expect((await planner.generate('find object name', [], adapter(4))).unsupportedFragments).toEqual([
    'find object name',
  ])
  expect((await planner.generate('find code', [], adapter(6))).unsupportedFragments).toEqual(['find code'])
})
it('handles explicit negation, duplicate tokens, empty cleaned values and schema-less summaries', async () => {
  const nodes = [
    makeNode({ id: 'a', schema: null, type: 'Procedures' }),
    makeNode({ id: 'b', type: 'StoredProcedures' }),
  ]
  const plan = await planner.generate(
    'not named "Orders" and not description "old" and procedures and procedures',
    nodes,
    adapter(4),
  )
  expect(plan.tokens).toContainEqual({ attribute: 'name', operator: 'is-not', values: ['Orders'] })
  expect(plan.tokens).toContainEqual({ attribute: 'description', operator: 'is-not', values: ['old'] })
  expect(plan.tokens.filter((token) => token.attribute === 'type')).toHaveLength(1)
  expect((await planner.generate('excluding procedures', nodes, adapter(4))).tokens[0].operator).toBe('is-not-in')
  expect(
    (await planner.generate('named in AppDb', nodes, adapter(4))).tokens.every((token) => token.attribute !== 'name'),
  ).toBe(true)
  expect((await planner.generate('mentions', nodes, adapter(6))).confidence).toBe('low')
  const summary = makeNode({ id: 'table', name: 'T', schema: null })
  summary.columnNames = ['Id']
  expect((await planner.generate('references to column Id from T', [summary], adapter(0))).columnReference).toEqual({
    objectId: 'table',
    column: 'Id',
  })
})
it('honors cancellation before planning and between clauses', async () => {
  const controller = new AbortController()
  controller.abort()
  await expect(planner.generate('query', [], adapter(0), controller.signal)).rejects.toMatchObject({
    name: 'AbortError',
  })
  const active = new AbortController()
  const embedding = adapter(4)
  embedding.embed = vi.fn(async () => {
    active.abort()
    return []
  })
  await expect(planner.generate('first and second', [], embedding, active.signal)).rejects.toMatchObject({
    name: 'AbortError',
  })
  expect((await planner.generate(' ', [], adapter(0))).warnings).toEqual(['Enter a filter request.'])
})
