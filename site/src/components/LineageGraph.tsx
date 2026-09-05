import { useMemo, useState } from 'react'
import { ReactFlow, Background, Controls, MiniMap, Panel, useReactFlow, type Node, type Edge } from '@xyflow/react'
import '@xyflow/react/dist/style.css'
import { Link, useNavigate } from 'react-router-dom'
import { useCatalog } from '../lib/CatalogContext'
import { useTheme } from '../lib/ThemeContext'
import { layoutGraph } from '../lib/layout'
import { colorForType } from '../lib/typeColors'
import { buildLineageGraphSvg, downloadPng, downloadSvg } from '../lib/graphExport'
import { bundleNeighborhood, isBundleId, type NeighborBundle } from '../lib/neighborhood'

interface EdgeColumnData extends Record<string, unknown> {
  from: string
  to: string
  columns: string[]
  /** Only dynamically-built SQL produced this edge (see CatalogEdge.dynamic). */
  dynamic?: boolean
}

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
  /**
   * Above this many nodes a focused graph collapses same-type neighbors into
   * counted bundle nodes rather than drawing every one of them. Ignored when
   * there's no focusId - there's nothing to bundle around.
   */
  maxNodes?: number
}

const EDGE_LABEL_CAP = 3

/** Past this the layout is slow and the picture is a hairball, so a focused view summarizes instead. */
const DEFAULT_MAX_NODES = 60

/** A single type group larger than this collapses once the graph is over its node budget. */
const MAX_PER_GROUP = 8

export default function LineageGraph({ nodeIds, focusId, height = 560, onNodeActivate, maxNodes = DEFAULT_MAX_NODES }: LineageGraphProps) {
  const { index } = useCatalog()
  const { theme } = useTheme()
  const navigate = useNavigate()
  const [selectedEdgeId, setSelectedEdgeId] = useState<string | null>(null)
  const [openBundleId, setOpenBundleId] = useState<string | null>(null)

  const bundling = useMemo(() => {
    if (!index || !focusId) return { nodeIds, bundles: [] as NeighborBundle[], bundledCount: 0 }
    return bundleNeighborhood(index, focusId, nodeIds, { maxNodes, maxPerGroup: MAX_PER_GROUP })
  }, [index, focusId, nodeIds, maxNodes])

  const { nodes, edges } = useMemo(() => {
    if (!index) return { nodes: [] as Node[], edges: [] as Edge<EdgeColumnData>[] }

    const shownIds = bundling.nodeIds
    const idSet = new Set(shownIds)
    const flowNodes: Node[] = shownIds
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

    const flowEdges: Edge<EdgeColumnData>[] = []
    for (const [from, targets] of index.outgoing.entries()) {
      if (!idSet.has(from)) continue
      for (const to of targets) {
        if (!idSet.has(to)) continue
        const columns = index.edgeColumns.get(`${from}|${to}`) ?? []
        const hasColumns = columns.length > 0
        // An edge only dynamically-built SQL produced is drawn dashed: the same
        // relationship, held with less certainty than one read off the parse tree.
        const dynamic = index.dynamicEdges.has(`${from}|${to}`)
        const label = hasColumns
          ? columns.length > EDGE_LABEL_CAP
            ? `${columns.slice(0, EDGE_LABEL_CAP).join(', ')}, +${columns.length - EDGE_LABEL_CAP} (click)`
            : columns.join(', ')
          : dynamic
            ? 'dynamic'
            : undefined
        flowEdges.push({
          id: `${from}->${to}`,
          source: from,
          target: to,
          animated: false,
          label,
          labelStyle: { fill: 'var(--text-muted)', fontSize: 10 },
          labelBgStyle: { fill: 'var(--surface)' },
          labelBgPadding: [3, 2],
          style: {
            stroke: hasColumns ? 'var(--accent)' : 'var(--border)',
            strokeWidth: hasColumns ? 1.5 : 1,
            strokeDasharray: dynamic ? '4 3' : undefined,
          },
          data: { from, to, columns, dynamic },
        })
      }
    }

    // Each collapsed group is one dashed node carrying its count, wired to the
    // focus in the direction its members sit - the shape of the fan-out stays
    // readable even when the names in it don't fit on a screen.
    for (const bundle of bundling.bundles) {
      const color = colorForType(bundle.type)
      flowNodes.push({
        id: bundle.id,
        data: { label: `${bundle.memberIds.length} ${bundle.type}` },
        position: { x: 0, y: 0 },
        style: {
          background: 'var(--surface-alt)',
          color: 'var(--text)',
          border: `2px dashed ${color}`,
          borderRadius: 2,
          padding: 8,
          fontSize: 12,
          width: 200,
          cursor: 'pointer',
        },
      })
      flowEdges.push({
        id: `bundle:${bundle.id}`,
        source: bundle.direction === 'outgoing' ? focusId! : bundle.id,
        target: bundle.direction === 'outgoing' ? bundle.id : focusId!,
        animated: false,
        style: { stroke: 'var(--border)', strokeWidth: 1, strokeDasharray: '4 3' },
        data: { from: focusId!, to: bundle.id, columns: [] },
      })
    }

    return { nodes: layoutGraph(flowNodes, flowEdges), edges: flowEdges }
  }, [index, bundling, focusId])

  const selectedEdge = edges.find((e) => e.id === selectedEdgeId)
  const selectedData = selectedEdge?.data
  const openBundle = bundling.bundles.find((bundle) => bundle.id === openBundleId)

  if (!index) return null
  if (nodes.length === 0) {
    return <div className="lineage-empty">No lineage relationships found for this selection.</div>
  }

  function activateNode(id: string) {
    if (isBundleId(id)) {
      setOpenBundleId((current) => (current === id ? null : id))
      setSelectedEdgeId(null)
      return
    }
    if (onNodeActivate) onNodeActivate(id)
    else navigate(`/object/${id}`)
  }

  return (
    <>
      {bundling.bundledCount > 0 && (
        <p className="muted lineage-bundle-hint">
          {bundling.bundledCount} neighbours are grouped into {bundling.bundles.length} dashed{' '}
          {bundling.bundles.length === 1 ? 'node' : 'nodes'} to keep this readable - click one to list what&apos;s inside
          it.
        </p>
      )}
      <div className="lineage-graph" style={{ height }}>
        <ReactFlow
          nodes={nodes}
          edges={edges}
          onNodeClick={(_, node) => activateNode(node.id)}
          onNodeDoubleClick={(_, node) => {
            if (!isBundleId(node.id)) navigate(`/object/${node.id}`)
          }}
          onEdgeClick={(_, edge) => {
            const data = edge.data as EdgeColumnData | undefined
            if (!data || data.columns.length === 0) return
            setSelectedEdgeId((current) => (current === edge.id ? null : edge.id))
          }}
          onPaneClick={() => {
            setSelectedEdgeId(null)
            setOpenBundleId(null)
          }}
          fitView
          colorMode={theme}
          proOptions={{ hideAttribution: true }}
        >
          <Background />
          <Controls showInteractive={false} />
          <MiniMap pannable zoomable maskColor={theme === 'dark' ? 'rgba(0, 0, 0, 0.7)' : 'rgba(255, 255, 255, 0.75)'} />
          <ExportControls />
        </ReactFlow>

        {openBundle && (
          <div className="lineage-edge-panel lineage-bundle-panel">
            <div className="lineage-edge-panel-title">
              <span>
                {openBundle.memberIds.length} {openBundle.type} {openBundle.direction === 'outgoing' ? 'this object depends on' : 'that use this object'}
              </span>
            </div>
            <ul className="related-list lineage-bundle-list">
              {openBundle.memberIds.map((id) => (
                <li key={id}>
                  <Link to={`/object/${id}`}>{index.byId.get(id)?.qualifiedName ?? id}</Link>
                </li>
              ))}
            </ul>
            <button type="button" className="lineage-edge-panel-close" aria-label="Close" onClick={() => setOpenBundleId(null)}>
              &times;
            </button>
          </div>
        )}

        {selectedData && selectedData.columns.length > 0 && (
          <div className="lineage-edge-panel">
            <div className="lineage-edge-panel-title">
              <span>{index.byId.get(selectedData.from)?.qualifiedName ?? selectedData.from}</span>
              <span className="lineage-edge-panel-arrow">&rarr;</span>
              <span>{index.byId.get(selectedData.to)?.qualifiedName ?? selectedData.to}</span>
              <span className="muted">references {selectedData.columns.length} column{selectedData.columns.length === 1 ? '' : 's'}:</span>
            </div>
            <span className="column-tags">
              {selectedData.columns.map((col) => (
                <span key={col} className="column-tag">
                  {col}
                </span>
              ))}
            </span>
            <button type="button" className="lineage-edge-panel-close" aria-label="Close" onClick={() => setSelectedEdgeId(null)}>
              &times;
            </button>
          </div>
        )}
      </div>
    </>
  )
}

function timestampForFilename(): string {
  return new Date().toISOString().slice(0, 19).replace(/[:T]/g, '-')
}

/**
 * Export-as-image toolbar for the current graph view, for incident write-ups
 * or design docs referencing a specific dependency chain. Reads live
 * measured node dimensions via useReactFlow() rather than the pre-render
 * `nodes`/`edges` this component computed, so the export matches what's
 * actually on screen.
 */
function ExportControls() {
  const { getNodes, getEdges } = useReactFlow()

  function handleExport(format: 'svg' | 'png') {
    const svg = buildLineageGraphSvg(getNodes(), getEdges())
    const stamp = timestampForFilename()
    if (format === 'svg') {
      downloadSvg(svg, `syncsql-lineage-${stamp}.svg`)
    } else {
      downloadPng(svg, `syncsql-lineage-${stamp}.png`)
    }
  }

  return (
    <Panel position="top-right" className="lineage-export-panel">
      <button type="button" className="lineage-export-btn" onClick={() => handleExport('svg')}>
        Export SVG
      </button>
      <button type="button" className="lineage-export-btn" onClick={() => handleExport('png')}>
        Export PNG
      </button>
    </Panel>
  )
}
