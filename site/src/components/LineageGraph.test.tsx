import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { ReactNode } from 'react'
import type { Edge, Node } from '@xyflow/react'
import LineageGraph from './LineageGraph'
import { buildIndex } from '../lib/catalog'
import { makeCatalog, makeEdge, makeNode } from '../test/fixtures'
import { buildLineageGraphSvg, downloadSvg } from '../lib/graphExport'
import type { CatalogIndex } from '../lib/catalog'

const graph = vi.hoisted(() => ({ nodes: [] as Node[], edges: [] as Edge[] }))
const context = vi.hoisted(() => ({ index: null as CatalogIndex | null }))
vi.mock('@xyflow/react', () => ({
  ReactFlow: ({ nodes, edges, onNodeClick, onEdgeClick, children }: { nodes: Node[]; edges: Edge[]; onNodeClick: (e: unknown, node: Node) => void; onEdgeClick: (e: unknown, edge: Edge) => void; children: ReactNode }) => {
    graph.nodes = nodes; graph.edges = edges
    return <div>{nodes.map(node => <button key={node.id} onClick={() => onNodeClick(null, node)}>Node {node.id}</button>)}{edges.map(edge => <button key={edge.id} onClick={() => onEdgeClick(null, edge)}>Edge {edge.id}</button>)}{children}</div>
  },
  Background: () => null, Controls: () => null, MiniMap: () => null,
  Panel: ({ children }: { children: ReactNode }) => <div>{children}</div>,
  useReactFlow: () => ({ getNodes: () => graph.nodes, getEdges: () => graph.edges }),
}))
vi.mock('../lib/CatalogContext', () => ({ useCatalog: () => context }))
vi.mock('../lib/graphExport', () => ({ buildLineageGraphSvg: vi.fn(() => '<svg/>'), downloadSvg: vi.fn(), downloadPng: vi.fn() }))

describe('graph integration retained in inspector layout', () => {
  beforeEach(() => {
    context.index = buildIndex(makeCatalog({ nodes: [makeNode({ id: 'a' }), makeNode({ id: 'b' })], edges: [makeEdge('a', 'b', ['Id', 'Total', 'Tax', 'Notes'])] }))
  })
  it('reports complete edge evidence and clears it on focus change while retaining drill actions', () => {
    const inspect = vi.fn(), drill = vi.fn()
    const view = render(<MemoryRouter><LineageGraph nodeIds={['a', 'b']} focusId="a" onNodeActivate={drill} onEdgeInspect={inspect} /></MemoryRouter>)
    expect(graph.nodes.find(node => node.id === 'a')?.className).toBe('lineage-node--focus')
    fireEvent.click(screen.getByRole('button', { name: 'Edge a->b' }))
    expect(graph.edges[0]).toMatchObject({ source: 'a', target: 'b', markerEnd: { type: 'arrowclosed' } })
    expect(inspect).toHaveBeenLastCalledWith(expect.objectContaining({ columns: ['Id', 'Total', 'Tax', 'Notes'] }))
    expect(screen.getByText('Notes')).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: 'Node b' }))
    expect(drill).toHaveBeenLastCalledWith('b')
    view.rerender(<MemoryRouter><LineageGraph nodeIds={['a', 'b']} focusId="b" onNodeActivate={drill} onEdgeInspect={inspect} /></MemoryRouter>)
    expect(inspect).toHaveBeenLastCalledWith(null)
    expect(screen.queryByText('Notes')).not.toBeInTheDocument()
    expect(graph.nodes.find(node => node.id === 'b')?.className).toBe('lineage-node--focus')
    expect(graph.nodes.find(node => node.id === 'a')?.className).toBeUndefined()
  })
  it('exports the displayed live graph with a timestamped filename', async () => {
    render(<MemoryRouter><LineageGraph nodeIds={['a', 'b']} focusId="a" /></MemoryRouter>)
    fireEvent.click(screen.getByRole('button', { name: 'Export SVG' }))
    await waitFor(() => expect(buildLineageGraphSvg).toHaveBeenLastCalledWith(graph.nodes, graph.edges))
    expect(downloadSvg).toHaveBeenLastCalledWith('<svg/>', expect.stringMatching(/^syncsql-lineage-.*\.svg$/))
  })
  it.each([false, true])('preserves outer connections and reverse edges when grouping is %s', (groupIntermediate) => {
    const ids = ['root', 'a', 'b', 'c', 'end']
    context.index = buildIndex(makeCatalog({ nodes: ids.map(id => makeNode({ id })), edges: [
      makeEdge('root', 'a'), makeEdge('root', 'b'), makeEdge('root', 'c'),
      makeEdge('a', 'end'), makeEdge('b', 'end'), makeEdge('c', 'end'), makeEdge('a', 'root'),
    ] }))
    const view = render(<MemoryRouter><LineageGraph nodeIds={ids} focusId="root" maxNodes={3} groupIntermediate={groupIntermediate} /></MemoryRouter>)
    const bundle = graph.nodes.find(node => node.id.startsWith('__bundle__'))!
    expect(bundle).toBeDefined()
    expect(graph.edges).toEqual(expect.arrayContaining([
      expect.objectContaining({ source: 'root', target: bundle.id, label: '3 references' }),
      expect.objectContaining({ source: bundle.id, target: 'end', label: '3 references' }),
      expect.objectContaining({ source: bundle.id, target: 'root', label: '1 reference' }),
    ]))
    for (const edge of graph.edges) {
      expect(graph.nodes.some(node => node.id === edge.source)).toBe(true)
      expect(graph.nodes.some(node => node.id === edge.target)).toBe(true)
    }
    fireEvent.click(screen.getByRole('button', { name: `Node ${bundle.id}` }))
    expect(screen.getAllByRole('link')).toHaveLength(3)
    view.rerender(<MemoryRouter><LineageGraph nodeIds={ids} focusId="root" maxNodes={60} groupIntermediate={false} /></MemoryRouter>)
    expect(graph.nodes).toHaveLength(5)
    expect(graph.edges).toHaveLength(7)
  })
})
