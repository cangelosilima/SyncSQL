import dagre from 'dagre'
import type { Node, Edge } from '@xyflow/react'

const NODE_WIDTH = 220
const NODE_HEIGHT = 56

export function layoutGraph(nodes: Node[], edges: Edge[], direction: 'LR' | 'TB' = 'LR'): Node[] {
  const graph = new dagre.graphlib.Graph()
  graph.setDefaultEdgeLabel(() => ({}))
  graph.setGraph({ rankdir: direction, nodesep: 60, ranksep: 140, edgesep: 30 })

  for (const node of nodes) {
    const label = String(node.data?.label ?? node.id)
    const height = Math.max(NODE_HEIGHT, Math.ceil(label.length / 26) * 18 + 20)
    graph.setNode(node.id, { width: NODE_WIDTH, height })
  }
  for (const edge of edges) {
    graph.setEdge(edge.source, edge.target)
  }

  dagre.layout(graph)

  return nodes.map((node) => {
    const pos = graph.node(node.id)
    return {
      ...node,
      sourcePosition: (direction === 'LR' ? 'right' : 'bottom') as Node['sourcePosition'],
      targetPosition: (direction === 'LR' ? 'left' : 'top') as Node['targetPosition'],
      style: { ...node.style, width: pos.width, height: pos.height, display: 'flex', alignItems: 'center', justifyContent: 'center', overflowWrap: 'anywhere', lineHeight: '18px' },
      position: { x: pos.x - pos.width / 2, y: pos.y - pos.height / 2 },
    }
  })
}

export { NODE_WIDTH, NODE_HEIGHT }
