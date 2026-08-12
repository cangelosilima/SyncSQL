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
}

export default function LineageGraph({ nodeIds, focusId, height = 560 }: LineageGraphProps) {
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
            borderRadius: 8,
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
        flowEdges.push({
          id: `${from}->${to}`,
          source: from,
          target: to,
          animated: false,
          style: { stroke: 'var(--border)' },
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
        onNodeClick={(_, node) => navigate(`/object/${node.id}`)}
        fitView
        proOptions={{ hideAttribution: true }}
      >
        <Background />
        <Controls showInteractive={false} />
        <MiniMap pannable zoomable />
      </ReactFlow>
    </div>
  )
}
