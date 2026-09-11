import { useEffect, useMemo, useState } from 'react'
import { ReactFlow, Background, Controls, Panel, useReactFlow, type Node, type Edge } from '@xyflow/react'
import '@xyflow/react/dist/style.css'
import { Link, useNavigate } from 'react-router-dom'
import { useCatalog } from '../lib/CatalogContext'
import { isLinkNode } from '../lib/catalog'
import { layoutGraph } from '../lib/layout'
import { colorForType } from '../lib/typeColors'
import { buildLineageGraphSvg, downloadPng, downloadSvg } from '../lib/graphExport'
import { bundleNeighborhood, groupIntermediateLayers, isBundleId, type NeighborBundle } from '../lib/neighborhood'

export interface EdgeColumnData extends Record<string, unknown> {
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
  onEdgeInspect?: (edge: EdgeColumnData | null) => void
  /**
   * Above this many nodes a focused graph collapses same-type neighbors into
   * counted bundle nodes rather than drawing every one of them. Ignored when
   * there's no focusId - there's nothing to bundle around.
   */
  maxNodes?: number
  groupIntermediate?: boolean
  connectorIds?: string[]
}

const EDGE_LABEL_CAP = 3

/** Past this the layout is slow and the picture is a hairball, so a focused view summarizes instead. */
const DEFAULT_MAX_NODES = 60

/** A single type group larger than this collapses once the graph is over its node budget. */
const MAX_PER_GROUP = 8
const NO_CONNECTORS: string[] = []

export default function LineageGraph({ nodeIds, focusId, height = 560, onNodeActivate, onEdgeInspect, maxNodes = DEFAULT_MAX_NODES, groupIntermediate = false, connectorIds = NO_CONNECTORS }: LineageGraphProps) {
  const { index } = useCatalog()
  const navigate = useNavigate()
  const [selectedEdgeId, setSelectedEdgeId] = useState<string | null>(null)
  const [openBundleId, setOpenBundleId] = useState<string | null>(null)

  const bundling = useMemo(() => {
    if (!index || !focusId) return { nodeIds, bundles: [] as NeighborBundle[], bundledCount: 0 }
    if (groupIntermediate) return groupIntermediateLayers(index, focusId, nodeIds)
    return bundleNeighborhood(index, focusId, nodeIds, { maxNodes, maxPerGroup: MAX_PER_GROUP })
  }, [index, focusId, nodeIds, maxNodes, groupIntermediate])

  const { nodes, edges } = useMemo(() => {
    if (!index) return { nodes: [] as Node[], edges: [] as Edge<EdgeColumnData>[] }

    const shownIds = bundling.nodeIds
    const idSet = new Set(nodeIds)
    const representative = new Map(bundling.bundles.flatMap(bundle => bundle.memberIds.map(id => [id, bundle.id] as const)))
    const flowNodes: Node[] = shownIds
      .map((id) => index.byId.get(id))
      .filter((n): n is NonNullable<typeof n> => Boolean(n))
      .map((node) => {
        const isFocus = node.id === focusId
        const color = colorForType(node.type)
        // Link names are local to the owning server (and database for Oracle).
        // The same remote name on two servers represents two distinct links.
        const label = isLinkNode(node)
          ? `${node.qualifiedName} (on ${node.server}${node.type === 'DatabaseLinks' ? ` / ${node.database}` : ''})`
          : node.qualifiedName
        return {
          id: node.id,
          className: isFocus ? 'lineage-node--focus' : undefined,
          ariaLabel: `${label}, ${node.type}${isFocus ? ', current focus' : ''}`,
          data: { label: `${label}${connectorIds.includes(node.id) ? ' (connecting object)' : ''}`, objectType: node.type },
          position: { x: 0, y: 0 },
          style: {
            background: isFocus ? 'var(--selected)' : 'var(--surface)',
            color: 'var(--text)',
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
        const source = representative.get(from) ?? from
        const target = representative.get(to) ?? to
        if (source === target && from !== to) continue
        if (source !== from || target !== to) {
          const id = `group:${source}->${target}`
          const existing = flowEdges.find(edge => edge.id === id)
          const count = Number(existing?.data?.referenceCount ?? 0) + 1
          if (existing) {
            existing.data!.referenceCount = count
            existing.label = `${count} references`
          } else {
            flowEdges.push({
              id, source, target,
              markerEnd: { type: 'arrowclosed', width: 20, height: 20, color: 'var(--text-muted)' },
              label: '1 reference',
              labelStyle: { fill: 'var(--text-muted)', fontSize: 10 },
              labelBgStyle: { fill: 'var(--surface)' },
              style: { stroke: 'var(--text-muted)', strokeWidth: 1.5 },
              data: { from: source, to: target, columns: [], referenceCount: count },
            })
          }
          continue
        }
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
          markerEnd: { type: 'arrowclosed', width: 20, height: 20, color: hasColumns ? 'var(--accent)' : 'var(--border)' },
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

    // Edges above are projected through each bundle, preserving outer-hop
    // connections, cross-links and cycles instead of inventing a focus-only star.
    for (const bundle of bundling.bundles) {
      const color = colorForType(bundle.type)
      flowNodes.push({
        id: bundle.id,
        ariaLabel: `${bundle.memberIds.length} grouped ${bundle.type}${bundle.hop ? `, hop ${bundle.hop}` : ''}`,
        data: { label: `${bundle.memberIds.length} ${bundle.type}${bundle.hop ? ` · ${bundle.direction === 'outgoing' ? 'Dependencies' : 'Dependents'} · hop ${bundle.hop}` : ''}`, objectType: bundle.type },
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
    }

    return { nodes: layoutGraph(flowNodes, flowEdges), edges: flowEdges }
  }, [index, bundling, focusId, nodeIds, connectorIds])

  const selectedEdge = edges.find((e) => e.id === selectedEdgeId)
  const selectedData = selectedEdge?.data
  useEffect(() => { onEdgeInspect?.(selectedData ?? null) }, [selectedData, onEdgeInspect])
  useEffect(() => { setSelectedEdgeId(null); setOpenBundleId(null) }, [focusId])
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
          it. Edges show recorded references between group members and connected objects.
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
          colorMode="light"
          proOptions={{ hideAttribution: true }}
        >
          <Background />
          <ExportControls />
          <FitGraph nodeIds={nodes.map(node => node.id).join('|')} focusId={focusId} />
        </ReactFlow>

        {openBundle && (
          <div className="lineage-edge-panel lineage-bundle-panel">
            <div className="lineage-edge-panel-title">
              <span>
                {openBundle.memberIds.length} {openBundle.type} · {openBundle.direction === 'outgoing' ? 'Dependencies' : 'Dependents'}{openBundle.hop ? ` · hop ${openBundle.hop}` : ''}
              </span>
            </div>
            <ul className="related-list lineage-bundle-list">
              {openBundle.memberIds.map((id) => (
                <li key={id}>
                  <Link to={`/object/${id}`}>{index.byId.get(id)?.qualifiedName ?? id}</Link>
                  {onNodeActivate && <button type="button" className="breadcrumb-link" onClick={() => activateNode(id)}>Focus</button>}
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

function FitGraph({ nodeIds, focusId }: { nodeIds: string; focusId?: string }) {
  const { fitView } = useReactFlow()
  useEffect(() => {
    const frame = requestAnimationFrame(() => {
      void fitView?.({ padding: 0.15, duration: window.matchMedia?.('(prefers-reduced-motion: reduce)').matches ? 0 : 250 })
    })
    return () => cancelAnimationFrame(frame)
  }, [nodeIds, focusId, fitView])
  return null
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
  const [exporting, setExporting] = useState(false)
  const [error, setError] = useState<string | null>(null)

  async function handleExport(format: 'svg' | 'png') {
    setExporting(true)
    setError(null)
    try {
      await document.fonts?.ready
      const svg = buildLineageGraphSvg(getNodes(), getEdges())
      const stamp = timestampForFilename()
      if (format === 'svg') downloadSvg(svg, `syncsql-lineage-${stamp}.svg`)
      else await downloadPng(svg, `syncsql-lineage-${stamp}.png`)
    } catch {
      setError('Graph export failed. Please try again.')
    } finally {
      setExporting(false)
    }
  }

  return (
    <Panel position="top-right" className="lineage-export-panel">
      <Controls showInteractive={false} orientation="horizontal" />
      {error && <span role="alert">{error}</span>}
      <button type="button" className="lineage-export-btn" disabled={exporting} onClick={() => handleExport('svg')}>
        Export SVG
      </button>
      <button type="button" className="lineage-export-btn" disabled={exporting} onClick={() => handleExport('png')}>
        Export PNG
      </button>
    </Panel>
  )
}
