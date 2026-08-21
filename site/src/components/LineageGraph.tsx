import { useMemo } from 'react'
import { ReactFlow, Background, Controls, MiniMap, type Node, type Edge } from '@xyflow/react'
import '@xyflow/react/dist/style.css'
import { useNavigate } from 'react-router-dom'
import { useCatalog } from '../lib/CatalogContext'
import { layoutGraph } from '../lib/layout'
import { colorForType } from '../lib/typeColors'

interface LineageGraphProps {
  nodeIds: string[]
  focusId?: string
  height?: number | string
  /**
   * Single-click handler: when provided, clicking a node drills the graph
   * into it (re-centering/expanding in place) instead of navigating away.
   * Double-click always opens the object's detail page regardless.
   */
  onNodeActivate?: (id: string) => void
}

export default function LineageGraph({ nodeIds, focusId, height = 560, onNodeActivate }: LineageGraphProps) {
  const { index } = useCatalog()
  const navigate = useNavigate()

  const { nodes, edges } = useMemo(() => {
    if (!index) return { nodes: [] as Node[], edges: [] as Edge[] }

    const idSet = new Set(nodeIds)
    const flowNodes: Node[] = nodeIds
      .map((id) => index.byId.get(id))
      .filter((n): n is NonNullable<typeof n> => Boolean(n))
      .map((node) => {
        const isFocus = node.id === focusId
        const color = colorForType(node.type)
        return {
          id: node.id,
          data: { label: node.qualifiedName },
          position: { x: 0, y: 0 },
          style: {
            background: isFocus ? color : 'var(--surface)',
            color: isFocus ? '#fff' : 'var(--text)',
            border: `2px solid ${color}`,
            borderRadius: 2,
            padding: 8,
            fontSize: 12,
            width: 200,
          },
        }
      })

    const flowEdges: Edge[] = []
    for (const [from, targets] of index.outgoing.entries()) {
      if (!idSet.has(from)) continue
      for (const to of targets) {
        if (!idSet.has(to)) continue
        const columns = index.edgeColumns.get(`${from}|${to}`) ?? []
        const hasColumns = columns.length > 0
        const label = hasColumns ? (columns.length > 3 ? `${columns.slice(0, 3).join(', ')}, +${columns.length - 3}` : columns.join(', ')) : undefined
        flowEdges.push({
          id: `${from}->${to}`,
          source: from,
          target: to,
          animated: false,
          label,
          labelStyle: { fill: 'var(--text-muted)', fontSize: 10 },
          labelBgStyle: { fill: 'var(--surface)' },
          labelBgPadding: [3, 2],
          style: { stroke: hasColumns ? 'var(--accent)' : 'var(--border)', strokeWidth: hasColumns ? 1.5 : 1 },
        })
      }
    }

    return { nodes: layoutGraph(flowNodes, flowEdges), edges: flowEdges }
  }, [index, nodeIds, focusId])

  if (!index) return null
  if (nodes.length === 0) {
    return <div className="lineage-empty">No lineage relationships found for this selection.</div>
  }

  return (
    <div className="lineage-graph" style={{ height }}>
      <ReactFlow
        nodes={nodes}
        edges={edges}
        onNodeClick={(_, node) => (onNodeActivate ? onNodeActivate(node.id) : navigate(`/object/${node.id}`))}
        onNodeDoubleClick={(_, node) => navigate(`/object/${node.id}`)}
        fitView
        colorMode="dark"
        proOptions={{ hideAttribution: true }}
      >
        <Background />
        <Controls showInteractive={false} />
        <MiniMap pannable zoomable maskColor="rgba(0, 0, 0, 0.7)" />
      </ReactFlow>
    </div>
  )
}
