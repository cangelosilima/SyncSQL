import { describe, expect, it } from 'vitest'
import { layoutGraph, NODE_WIDTH } from './layout'

describe('lineage layout', () => {
  it('aligns horizontal handles and rendered dimensions with layout bounds without changing edge direction', () => {
    const edges = [{ id: 'a-b', source: 'a', target: 'b' }]
    const nodes = layoutGraph([
      { id: 'a', data: { label: 'dbo.ProcedureWithAVeryLongName'.repeat(3) }, position: { x: 0, y: 0 } },
      { id: 'b', data: { label: 'dbo.Orders' }, position: { x: 0, y: 0 } },
    ], edges)
    expect(nodes[0]).toMatchObject({ sourcePosition: 'right', targetPosition: 'left', style: { width: NODE_WIDTH } })
    expect(Number(nodes[0].style?.height)).toBeGreaterThan(Number(nodes[1].style?.height))
    expect(nodes[1].position.x).toBeGreaterThan(nodes[0].position.x + NODE_WIDTH)
    expect(edges).toEqual([{ id: 'a-b', source: 'a', target: 'b' }])
  })
  it('aligns handles vertically when a top-to-bottom layout is requested', () => {
    expect(layoutGraph([{ id: 'a', data: {}, position: { x: 0, y: 0 } }], [], 'TB')[0]).toMatchObject({ sourcePosition: 'bottom', targetPosition: 'top' })
  })
})
