import { fireEvent, render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { describe, expect, it, vi } from 'vitest'
import type { ReactNode } from 'react'
import type { Edge, Node } from '@xyflow/react'
import LineageGraph from './LineageGraph'
import { buildIndex } from '../lib/catalog'
import { makeCatalog, makeEdge, makeNode } from '../test/fixtures'
import { buildLineageGraphSvg, downloadSvg } from '../lib/graphExport'

const graph = vi.hoisted(() => ({ nodes: [] as Node[], edges: [] as Edge[] }))
vi.mock('@xyflow/react', () => ({
  ReactFlow: ({ nodes, edges, onNodeClick, onEdgeClick, children }: { nodes: Node[]; edges: Edge[]; onNodeClick: (e: unknown, node: Node) => void; onEdgeClick: (e: unknown, edge: Edge) => void; children: ReactNode }) => {
    graph.nodes = nodes; graph.edges = edges
    return <div>{nodes.map(node => <button key={node.id} onClick={() => onNodeClick(null, node)}>Node {node.id}</button>)}{edges.map(edge => <button key={edge.id} onClick={() => onEdgeClick(null, edge)}>Edge {edge.id}</button>)}{children}</div>
  },
  Background: () => null, Controls: () => null, MiniMap: () => null,
  Panel: ({ children }: { children: ReactNode }) => <div>{children}</div>,
  useReactFlow: () => ({ getNodes: () => graph.nodes, getEdges: () => graph.edges }),
}))
vi.mock('../lib/CatalogContext', () => ({ useCatalog: () => ({ index: buildIndex(makeCatalog({ nodes: [makeNode({ id: 'a' }), makeNode({ id: 'b' })], edges: [makeEdge('a', 'b', ['Id', 'Total', 'Tax', 'Notes'])] })) }) }))
vi.mock('../lib/ThemeContext', () => ({ useTheme: () => ({ theme: 'light' }) }))
vi.mock('../lib/graphExport', () => ({ buildLineageGraphSvg: vi.fn(() => '<svg/>'), downloadSvg: vi.fn(), downloadPng: vi.fn() }))

describe('graph integration retained in inspector layout', () => {
  it('reports complete edge evidence and clears it on focus change while retaining drill actions', () => {
    const inspect = vi.fn(), drill = vi.fn()
    const view = render(<MemoryRouter><LineageGraph nodeIds={['a', 'b']} focusId="a" onNodeActivate={drill} onEdgeInspect={inspect} /></MemoryRouter>)
    fireEvent.click(screen.getByRole('button', { name: 'Edge a->b' }))
    expect(graph.edges[0]).toMatchObject({ source: 'a', target: 'b', markerEnd: { type: 'arrowclosed' } })
    expect(inspect).toHaveBeenLastCalledWith(expect.objectContaining({ columns: ['Id', 'Total', 'Tax', 'Notes'] }))
    expect(screen.getByText('Notes')).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: 'Node b' }))
    expect(drill).toHaveBeenLastCalledWith('b')
    view.rerender(<MemoryRouter><LineageGraph nodeIds={['a', 'b']} focusId="b" onNodeActivate={drill} onEdgeInspect={inspect} /></MemoryRouter>)
    expect(inspect).toHaveBeenLastCalledWith(null)
    expect(screen.queryByText('Notes')).not.toBeInTheDocument()
  })
  it('exports the displayed live graph with a timestamped filename', () => {
    render(<MemoryRouter><LineageGraph nodeIds={['a', 'b']} focusId="a" /></MemoryRouter>)
    fireEvent.click(screen.getByRole('button', { name: 'Export SVG' }))
    expect(buildLineageGraphSvg).toHaveBeenLastCalledWith(graph.nodes, graph.edges)
    expect(downloadSvg).toHaveBeenLastCalledWith('<svg/>', expect.stringMatching(/^syncsql-lineage-.*\.svg$/))
  })
})
