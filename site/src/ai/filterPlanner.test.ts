import { describe, expect, it } from 'vitest'
import type { CatalogNode } from '../types'
import type { EmbeddingAdapter, EmbeddingLoadOptions } from './types'
import { EmbeddingFilterPlanner } from './filterPlanner'

const nodes: CatalogNode[] = [
  node('SQLPROD01', 'AppDb', 'dbo', 'Tables', 'Orders'),
  node('SQLPROD01', 'AppDb', 'dbo', 'StoredProcedures', 'usp_GetOrders'),
  node('ORAPROD01', 'ORCLPDB1', 'APP', 'Procedures', 'GET_CUSTOMERS'),
]

const adapter: EmbeddingAdapter = {
  load: (_options: EmbeddingLoadOptions) => Promise.resolve(),
  embed: (texts) => Promise.resolve(texts.map(() => [0, 0, 0])),
  dispose: () => undefined,
}

describe('EmbeddingFilterPlanner', () => {
  it('creates metadata and DDL filters from one request', async () => {
    const plan = await new EmbeddingFilterPlanner().generate(
      'Show stored procedures in AppDb that mention Orders',
      nodes,
      adapter,
    )

    expect(plan.tokens).toEqual([
      { attribute: 'database', operator: 'is', values: ['AppDb'] },
      { attribute: 'type', operator: 'is', values: ['StoredProcedures'] },
    ])
    expect(plan.contentQuery).toBe('Orders')
    expect(plan.confidence).toBe('high')
  })

  it('scopes negation to the value that follows it', async () => {
    const plan = await new EmbeddingFilterPlanner().generate(
      'Show tables on SQLPROD01 except schema dbo',
      nodes,
      adapter,
    )

    expect(plan.tokens).toContainEqual({ attribute: 'server', operator: 'is', values: ['SQLPROD01'] })
    expect(plan.tokens).toContainEqual({ attribute: 'schema', operator: 'is-not', values: ['dbo'] })
    expect(plan.tokens).toContainEqual({ attribute: 'type', operator: 'is', values: ['Tables'] })
  })

  it('maps a generic procedure alias to every applicable catalog type', async () => {
    const plan = await new EmbeddingFilterPlanner().generate('Show procedures', nodes, adapter)
    expect(plan.tokens).toEqual([
      { attribute: 'type', operator: 'is-in', values: ['StoredProcedures', 'Procedures'] },
    ])
  })

  it('does not invent values for an unsupported request', async () => {
    const plan = await new EmbeddingFilterPlanner().generate('Show the risky things', nodes, adapter)
    expect(plan.tokens).toEqual([])
    expect(plan.confidence).toBe('low')
    expect(plan.unsupportedFragments).toEqual(['Show the risky things'])
  })

  it('blocks multiple independent DDL phrases', async () => {
    const plan = await new EmbeddingFilterPlanner().generate('mentions Orders and references Customers', nodes, adapter)
    expect(plan.contentQuery).toBe('')
    expect(plan.warnings).toContain('Only one DDL content phrase is supported at a time.')
  })
})

function node(server: string, database: string, schema: string, type: string, name: string): CatalogNode {
  return {
    id: `${server}/${database}/${schema}/${type}/${name}`,
    server,
    database,
    schema,
    type,
    name,
    qualifiedName: `${schema}.${name}`,
    path: '',
    ddl: '',
    description: null,
    columns: [],
    grants: [],
    sections: [],
    sizeBytes: 0,
    changeCount: 0,
    lastChangedAt: null,
    history: [],
    metrics: [],
  }
}
