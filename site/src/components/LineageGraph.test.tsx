import { act, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { ReactNode } from 'react'
import type { Edge, Node } from '@xyflow/react'
import LineageGraph from './LineageGraph'
import { buildIndex } from '../lib/catalog'
import { makeCatalog, makeEdge, makeNode } from '../test/fixtures'
import { buildLineageGraphSvg, downloadPng, downloadSvg } from '../lib/graphExport'
import type { CatalogIndex } from '../lib/catalog'

const graph = vi.hoisted(() => ({ nodes: [] as Node[], edges: [] as Edge[], fitView: vi.fn() }))
const context = vi.hoisted(() => ({ index: null as CatalogIndex | null }))
vi.mock('@xyflow/react', () => ({
  ReactFlow: ({
    nodes,
    edges,
    onNodeClick,
    onEdgeClick,
    onNodeDoubleClick,
    onPaneClick,
    children,
  }: {
    nodes: Node[]
    edges: Edge[]
    onNodeClick: (e: unknown, node: Node) => void
    onEdgeClick: (e: unknown, edge: Edge) => void
    onNodeDoubleClick: (e: unknown, node: Node) => void
    onPaneClick: () => void
    children: ReactNode
  }) => {
    graph.nodes = nodes
    graph.edges = edges
    return (
      <div>
        {nodes.map((node) => (
          <button
            key={node.id}
            onClick={() => onNodeClick(null, node)}
            onDoubleClick={() => onNodeDoubleClick(null, node)}
          >
            Node {node.id}
          </button>
        ))}
        {edges.map((edge) => (
          <button key={edge.id} onClick={() => onEdgeClick(null, edge)}>
            Edge {edge.id}
          </button>
        ))}
        {children}
        <button onClick={onPaneClick}>Pane</button>
        <button onClick={() => onEdgeClick(null, { id: 'none', source: 'a', target: 'b' })}>No evidence</button>
      </div>
    )
  },
  Background: () => null,
  Controls: () => null,
  MiniMap: () => null,
  Panel: ({ children }: { children: ReactNode }) => <div>{children}</div>,
  useReactFlow: () => ({ getNodes: () => graph.nodes, getEdges: () => graph.edges, fitView: graph.fitView }),
}))
vi.mock('../lib/CatalogContext', () => ({ useCatalog: () => context }))
vi.mock('../lib/graphExport', () => ({
  buildLineageGraphSvg: vi.fn(() => '<svg/>'),
  downloadSvg: vi.fn(),
  downloadPng: vi.fn(),
}))

describe('graph integration retained in inspector layout', () => {
  beforeEach(() => {
    context.index = buildIndex(
      makeCatalog({
        nodes: [makeNode({ id: 'a' }), makeNode({ id: 'b' })],
        edges: [makeEdge('a', 'b', ['Id', 'Total', 'Tax', 'Notes'])],
      }),
    )
  })
  it('handles absent catalogs and empty selections', () => {
    const saved = context.index
    context.index = null
    const { container, rerender } = render(
      <MemoryRouter>
        <LineageGraph nodeIds={[]} />
      </MemoryRouter>,
    )
    expect(container).toBeEmptyDOMElement()
    context.index = saved
    rerender(
      <MemoryRouter>
        <LineageGraph nodeIds={[]} />
      </MemoryRouter>,
    )
    expect(screen.getByText('No lineage relationships found for this selection.')).toBeVisible()
  })
  it('groups both directions, suppresses internal bundle edges, and focuses a member', () => {
    const ids = ['root', 'a', 'b', 'c', 'end', 'x', 'y', 'start']
    context.index = buildIndex(
      makeCatalog({
        nodes: ids.map((id) => makeNode({ id })),
        edges: [
          makeEdge('root', 'a'),
          makeEdge('root', 'b'),
          makeEdge('root', 'c'),
          makeEdge('a', 'b'),
          makeEdge('a', 'a'),
          makeEdge('a', 'end'),
          makeEdge('b', 'end'),
          makeEdge('c', 'end'),
          makeEdge('x', 'root'),
          makeEdge('y', 'root'),
          makeEdge('start', 'x'),
          makeEdge('start', 'y'),
        ],
      }),
    )
    const focus = vi.fn()
    render(
      <MemoryRouter>
        <LineageGraph nodeIds={ids} focusId="root" groupIntermediate onNodeActivate={focus} />
      </MemoryRouter>,
    )
    const bundles = graph.nodes.filter((node) => node.id.startsWith('__bundle__'))
    expect(bundles).toHaveLength(2)
    const incoming = bundles.find((node) => node.id.includes('incoming'))!
    expect(incoming.data.label).toContain('Dependents')
    fireEvent.click(screen.getByRole('button', { name: `Node ${incoming.id}` }))
    expect(screen.getAllByRole('link')).toHaveLength(2)
    fireEvent.click(screen.getAllByRole('button', { name: 'Focus' })[0])
    expect(focus).toHaveBeenCalledWith('x')
    expect(graph.edges.find((edge) => edge.source === edge.target)?.data?.referenceCount).toBe(1)
  })
  it('labels dangling edge evidence by its recorded identifiers', () => {
    context.index = buildIndex(
      makeCatalog({ nodes: [makeNode({ id: 'present' })], edges: [makeEdge('missing', 'target', ['Id'])] }),
    )
    render(
      <MemoryRouter>
        <LineageGraph nodeIds={['missing', 'target', 'present']} />
      </MemoryRouter>,
    )
    fireEvent.click(screen.getByRole('button', { name: 'Edge missing->target' }))
    expect(screen.getByText('missing', { selector: 'span' })).toBeVisible()
    expect(screen.getByText('target', { selector: 'span' })).toBeVisible()
  })
  it('renders link ownership and connector labels, dynamic edges, and dismisses column evidence', () => {
    context.index = buildIndex(
      makeCatalog({
        nodes: [
          makeNode({ id: 'a', type: 'DatabaseLinks' }),
          makeNode({ id: 'b', type: 'LinkedServers' }),
          makeNode({ id: 'outside' }),
        ],
        edges: [
          { ...makeEdge('a', 'b', ['Id']), dynamic: true },
          { ...makeEdge('b', 'a'), dynamic: true },
          makeEdge('a', 'outside'),
          makeEdge('outside', 'a'),
        ],
      }),
    )
    render(
      <MemoryRouter>
        <LineageGraph nodeIds={['a', 'b']} connectorIds={['a']} />
      </MemoryRouter>,
    )
    expect(graph.nodes[0].data.label).toContain('(on SRV1 / AppDb) (connecting object)')
    expect(graph.nodes[1].data.label).toContain('(on SRV1)')
    expect(graph.edges.find((edge) => edge.source === 'b')?.label).toBe('dynamic')
    fireEvent.click(screen.getByRole('button', { name: 'No evidence' }))
    fireEvent.click(screen.getByRole('button', { name: 'Edge b->a' }))
    const edge = screen.getByRole('button', { name: 'Edge a->b' })
    fireEvent.click(edge)
    expect(screen.getByText('references 1 column:')).toBeVisible()
    fireEvent.click(edge)
    expect(screen.queryByText('references 1 column:')).not.toBeInTheDocument()
    fireEvent.click(edge)
    fireEvent.click(screen.getByRole('button', { name: 'Close' }))
    fireEvent.click(edge)
    fireEvent.click(screen.getByRole('button', { name: 'Pane' }))
    expect(screen.queryByText('references 1 column:')).not.toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: 'Node b' }))
    fireEvent.doubleClick(screen.getByRole('button', { name: 'Node a' }))
  })
  it.each([true, false])('fits graphs with reduced motion=%s and reports failed PNG exports', async (matches) => {
    vi.stubGlobal('matchMedia', () => ({ matches }))
    const frames: FrameRequestCallback[] = []
    vi.stubGlobal('requestAnimationFrame', (callback: FrameRequestCallback) => {
      frames.push(callback)
      return 1
    })
    render(
      <MemoryRouter>
        <LineageGraph nodeIds={['a', 'b']} />
      </MemoryRouter>,
    )
    act(() => frames.forEach((frame) => frame(0)))
    expect(graph.fitView).toHaveBeenLastCalledWith({ padding: 0.15, duration: matches ? 0 : 250 })
    vi.mocked(downloadPng).mockRejectedValueOnce(new Error('failed'))
    fireEvent.click(screen.getByRole('button', { name: 'Export PNG' }))
    expect(await screen.findByRole('alert')).toHaveTextContent('Graph export failed')
    fireEvent.click(screen.getByRole('button', { name: 'Export PNG' }))
    await waitFor(() => expect(screen.queryByRole('alert')).not.toBeInTheDocument())
    vi.unstubAllGlobals()
  })
  it('reports complete edge evidence and clears it on focus change while retaining drill actions', () => {
    const inspect = vi.fn(),
      drill = vi.fn()
    const view = render(
      <MemoryRouter>
        <LineageGraph nodeIds={['a', 'b']} focusId="a" onNodeActivate={drill} onEdgeInspect={inspect} />
      </MemoryRouter>,
    )
    expect(graph.nodes.find((node) => node.id === 'a')?.className).toBe('lineage-node--focus')
    fireEvent.click(screen.getByRole('button', { name: 'Edge a->b' }))
    expect(graph.edges[0]).toMatchObject({ source: 'a', target: 'b', markerEnd: { type: 'arrowclosed' } })
    expect(inspect).toHaveBeenLastCalledWith(expect.objectContaining({ columns: ['Id', 'Total', 'Tax', 'Notes'] }))
    expect(screen.getByText('Notes')).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: 'Node b' }))
    expect(drill).toHaveBeenLastCalledWith('b')
    view.rerender(
      <MemoryRouter>
        <LineageGraph nodeIds={['a', 'b']} focusId="b" onNodeActivate={drill} onEdgeInspect={inspect} />
      </MemoryRouter>,
    )
    expect(inspect).toHaveBeenLastCalledWith(null)
    expect(screen.queryByText('Notes')).not.toBeInTheDocument()
    expect(graph.nodes.find((node) => node.id === 'b')?.className).toBe('lineage-node--focus')
    expect(graph.nodes.find((node) => node.id === 'a')?.className).toBeUndefined()
  })
  it('exports the displayed live graph with a timestamped filename', async () => {
    render(
      <MemoryRouter>
        <LineageGraph nodeIds={['a', 'b']} focusId="a" />
      </MemoryRouter>,
    )
    fireEvent.click(screen.getByRole('button', { name: 'Export SVG' }))
    await waitFor(() => expect(buildLineageGraphSvg).toHaveBeenLastCalledWith(graph.nodes, graph.edges))
    expect(downloadSvg).toHaveBeenLastCalledWith('<svg/>', expect.stringMatching(/^syncsql-lineage-.*\.svg$/))
  })
  it.each([false, true])('preserves outer connections and reverse edges when grouping is %s', (groupIntermediate) => {
    const ids = ['root', 'a', 'b', 'c', 'end']
    context.index = buildIndex(
      makeCatalog({
        nodes: ids.map((id) => makeNode({ id })),
        edges: [
          makeEdge('root', 'a'),
          makeEdge('root', 'b'),
          makeEdge('root', 'c'),
          makeEdge('a', 'end'),
          makeEdge('b', 'end'),
          makeEdge('c', 'end'),
          makeEdge('a', 'root'),
        ],
      }),
    )
    const view = render(
      <MemoryRouter>
        <LineageGraph nodeIds={ids} focusId="root" maxNodes={3} groupIntermediate={groupIntermediate} />
      </MemoryRouter>,
    )
    const bundle = graph.nodes.find((node) => node.id.startsWith('__bundle__'))!
    expect(bundle).toBeDefined()
    expect(graph.edges).toEqual(
      expect.arrayContaining([
        expect.objectContaining({ source: 'root', target: bundle.id, label: '3 references' }),
        expect.objectContaining({ source: bundle.id, target: 'end', label: '3 references' }),
        expect.objectContaining({ source: bundle.id, target: 'root', label: '1 reference' }),
      ]),
    )
    for (const edge of graph.edges) {
      expect(graph.nodes.some((node) => node.id === edge.source)).toBe(true)
      expect(graph.nodes.some((node) => node.id === edge.target)).toBe(true)
    }
    fireEvent.click(screen.getByRole('button', { name: `Node ${bundle.id}` }))
    expect(screen.getAllByRole('link')).toHaveLength(3)
    fireEvent.doubleClick(screen.getByRole('button', { name: `Node ${bundle.id}` }))
    fireEvent.click(screen.getByRole('button', { name: `Node ${bundle.id}` }))
    expect(screen.queryByRole('link')).not.toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: `Node ${bundle.id}` }))
    fireEvent.click(screen.getByRole('button', { name: 'Close' }))
    view.rerender(
      <MemoryRouter>
        <LineageGraph nodeIds={ids} focusId="root" maxNodes={60} groupIntermediate={false} />
      </MemoryRouter>,
    )
    expect(graph.nodes).toHaveLength(5)
    expect(graph.edges).toHaveLength(7)
  })
})
