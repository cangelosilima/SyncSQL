import type { Edge, Node } from '@xyflow/react'

interface ExportNode {
  id: string
  x: number
  y: number
  width: number
  height: number
  label: string
  background: string
  border: string
  textColor: string
}

interface ExportEdge {
  from: string
  to: string
  label?: string
  stroke: string
}

const DEFAULT_NODE_WIDTH = 200
const DEFAULT_NODE_HEIGHT = 40
const PADDING = 40

/** Resolves a `var(--token)` reference to its actual computed color, so the exported file renders standalone (no site stylesheet). */
function resolveColor(value: string): string {
  const match = /var\((--[a-z0-9-]+)\)/i.exec(value)
  if (!match) return value
  const resolved = getComputedStyle(document.documentElement).getPropertyValue(match[1]).trim()
  return resolved || value
}

function borderColorOf(style: Record<string, unknown> | undefined): string {
  const border = String(style?.border ?? '1px solid #999')
  const parts = border.trim().split(/\s+/)
  return resolveColor(parts[parts.length - 1] ?? '#999')
}

function nodeLabel(node: Node): string {
  const data = node.data as Record<string, unknown> | undefined
  return typeof data?.label === 'string' ? data.label : node.id
}

function collectExportNodes(nodes: Node[]): ExportNode[] {
  return nodes.map((n) => {
    const style = (n.style ?? {}) as Record<string, unknown>
    const measured = (n as { measured?: { width?: number; height?: number } }).measured
    return {
      id: n.id,
      x: n.position.x,
      y: n.position.y,
      width: measured?.width ?? (style.width as number | undefined) ?? DEFAULT_NODE_WIDTH,
      height: measured?.height ?? (style.height as number | undefined) ?? DEFAULT_NODE_HEIGHT,
      label: nodeLabel(n),
      background: resolveColor(String(style.background ?? 'var(--surface)')),
      border: borderColorOf(style),
      textColor: resolveColor(String(style.color ?? 'var(--text)')),
    }
  })
}

function collectExportEdges(edges: Edge[]): ExportEdge[] {
  return edges.map((e) => {
    const style = e.style as Record<string, unknown> | undefined
    return {
      from: e.source,
      to: e.target,
      label: typeof e.label === 'string' ? e.label : undefined,
      stroke: resolveColor(String(style?.stroke ?? 'var(--border)')),
    }
  })
}

function escapeXml(s: string): string {
  return s.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;')
}

function truncate(s: string, n: number): string {
  return s.length > n ? `${s.slice(0, n - 1)}…` : s
}

function colorMarkerId(color: string): string {
  return `arrow-${color.replace(/[^a-zA-Z0-9]/g, '')}`
}

export interface GraphSvg {
  markup: string
  width: number
  height: number
}

/**
 * Renders the current lineage graph as a standalone SVG string - built
 * directly from node positions/dimensions rather than rasterizing the live
 * DOM, so it stays a dependency-free vector export (no html-to-image/
 * dom-to-image) consistent with this app's other from-scratch visuals (see
 * TrendChart). Colors are resolved from CSS custom properties at call time
 * so the file renders correctly outside the site (no stylesheet to inherit
 * from) and matches whichever theme is currently active.
 */
export function buildLineageGraphSvg(rawNodes: Node[], rawEdges: Edge[]): GraphSvg {
  const nodes = collectExportNodes(rawNodes)
  const edges = collectExportEdges(rawEdges)
  const background = resolveColor('var(--surface-alt)')

  if (nodes.length === 0) {
    const markup = `<svg xmlns="http://www.w3.org/2000/svg" width="240" height="80" viewBox="0 0 240 80"><rect width="240" height="80" fill="${background}"/><text x="12" y="44" font-family="ui-monospace, monospace" font-size="13">No nodes to export.</text></svg>`
    return { markup, width: 240, height: 80 }
  }

  const minX = Math.min(...nodes.map((n) => n.x)) - PADDING
  const minY = Math.min(...nodes.map((n) => n.y)) - PADDING
  const maxX = Math.max(...nodes.map((n) => n.x + n.width)) + PADDING
  const maxY = Math.max(...nodes.map((n) => n.y + n.height)) + PADDING
  const width = maxX - minX
  const height = maxY - minY

  const byId = new Map(nodes.map((n) => [n.id, n]))
  const uniqueStrokes = [...new Set(edges.map((e) => e.stroke))]
  const markerDefs = uniqueStrokes
    .map(
      (c) =>
        `<marker id="${colorMarkerId(c)}" viewBox="0 0 10 10" refX="9" refY="5" markerWidth="7" markerHeight="7" orient="auto-start-reverse"><path d="M0,0 L10,5 L0,10 z" fill="${c}" /></marker>`,
    )
    .join('')

  const edgeMarkup = edges
    .map((e) => {
      const from = byId.get(e.from)
      const to = byId.get(e.to)
      if (!from || !to) return ''
      const x1 = from.x + from.width
      const y1 = from.y + from.height / 2
      const x2 = to.x
      const y2 = to.y + to.height / 2
      const midX = (x1 + x2) / 2
      const midY = (y1 + y2) / 2
      const label = e.label
        ? `<text x="${midX.toFixed(1)}" y="${(midY - 4).toFixed(1)}" font-family="ui-monospace, monospace" font-size="10" fill="${e.stroke}" text-anchor="middle">${escapeXml(e.label)}</text>`
        : ''
      return `<line x1="${x1.toFixed(1)}" y1="${y1.toFixed(1)}" x2="${x2.toFixed(1)}" y2="${y2.toFixed(1)}" stroke="${e.stroke}" stroke-width="1.5" marker-end="url(#${colorMarkerId(e.stroke)})" />${label}`
    })
    .join('\n')

  const nodeMarkup = nodes
    .map(
      (n) => `<g>
        <rect x="${n.x.toFixed(1)}" y="${n.y.toFixed(1)}" width="${n.width}" height="${n.height}" rx="2" fill="${n.background}" stroke="${n.border}" stroke-width="2" />
        <text x="${(n.x + n.width / 2).toFixed(1)}" y="${(n.y + n.height / 2).toFixed(1)}" font-family="ui-monospace, monospace" font-size="12" fill="${n.textColor}" text-anchor="middle" dominant-baseline="middle">${escapeXml(truncate(n.label, 34))}</text>
      </g>`,
    )
    .join('\n')

  const markup = `<svg xmlns="http://www.w3.org/2000/svg" viewBox="${minX.toFixed(1)} ${minY.toFixed(1)} ${width.toFixed(1)} ${height.toFixed(1)}" width="${width.toFixed(1)}" height="${height.toFixed(1)}">
    <defs>${markerDefs}</defs>
    <rect x="${minX.toFixed(1)}" y="${minY.toFixed(1)}" width="${width.toFixed(1)}" height="${height.toFixed(1)}" fill="${background}" />
    ${edgeMarkup}
    ${nodeMarkup}
  </svg>`

  return { markup, width, height }
}

function triggerDownload(blob: Blob, filename: string) {
  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url
  a.download = filename
  document.body.appendChild(a)
  a.click()
  document.body.removeChild(a)
  URL.revokeObjectURL(url)
}

export function downloadSvg(svg: GraphSvg, filename: string) {
  triggerDownload(new Blob([svg.markup], { type: 'image/svg+xml;charset=utf-8' }), filename)
}

/** Rasterizes the exported SVG to a PNG via an offscreen canvas - no extra dependency needed since every color in the SVG is already a literal (resolved from CSS variables), so nothing is lost off-page. */
export function downloadPng(svg: GraphSvg, filename: string, scale = 2) {
  const svgBlob = new Blob([svg.markup], { type: 'image/svg+xml;charset=utf-8' })
  const url = URL.createObjectURL(svgBlob)
  const img = new Image()
  img.onload = () => {
    const canvas = document.createElement('canvas')
    canvas.width = Math.ceil(svg.width * scale)
    canvas.height = Math.ceil(svg.height * scale)
    const ctx = canvas.getContext('2d')
    if (!ctx) {
      URL.revokeObjectURL(url)
      return
    }
    ctx.scale(scale, scale)
    ctx.drawImage(img, 0, 0, svg.width, svg.height)
    canvas.toBlob((blob) => {
      URL.revokeObjectURL(url)
      if (blob) triggerDownload(blob, filename)
    }, 'image/png')
  }
  img.onerror = () => URL.revokeObjectURL(url)
  img.src = url
}
