import { getBezierPath, Position, type Edge, type Node } from '@xyflow/react'
import { GRAPH_LEGEND_ITEMS, GRAPH_LEGEND_NOTE } from './graphLegend'
import { colorForType } from './typeColors'

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
  dashed: boolean
  focus: boolean
  fontSize: number
  lineHeight: number
}

interface ExportEdge {
  from: string
  to: string
  label?: string
  stroke: string
  strokeWidth: number | string
  dashed?: string
  labelColor: string
  labelBackground: string
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
      dashed: String(style.border).includes('dashed'),
      focus: n.className?.split(' ').includes('lineage-node--focus') ?? false,
      fontSize: Number(style.fontSize ?? 12),
      lineHeight: parseFloat(String(style.lineHeight ?? 18)),
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
      strokeWidth: String(style?.strokeWidth ?? 1),
      dashed: style?.strokeDasharray ? String(style.strokeDasharray) : undefined,
      labelColor: resolveColor(String(e.labelStyle?.fill ?? 'var(--text-muted)')),
      labelBackground: resolveColor(String(e.labelBgStyle?.fill ?? 'var(--surface)')),
    }
  })
}

function escapeXml(s: string): string {
  return s.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;')
}

function wrapLabel(label: string, width: number, measure: (value: string) => number): string[] {
  const lines: string[] = []
  for (const paragraph of label.split('\n')) {
    let line = ''
    for (const word of paragraph.split(/\s+/)) {
      const candidate = line ? `${line} ${word}` : word
      if (measure(candidate) <= width) {
        line = candidate
        continue
      }
      if (line) {
        lines.push(line)
        line = ''
      }
      for (const char of word) {
        if (line && measure(line + char) > width) {
          lines.push(line)
          line = ''
        }
        line += char
      }
    }
    lines.push(line)
  }
  return lines
}

function colorMarkerId(color: string): string {
  return `arrow-${color.replace(/[^a-zA-Z0-9]/g, '')}`
}

export interface GraphSvg {
  markup: string
  width: number
  height: number
}

/** A separate footer stays readable without covering nodes or depending on UI state. */
function buildLegend(
  x: number,
  y: number,
  width: number,
  font: string,
  types: string[],
  measure: (value: string, size: number) => number,
) {
  const color = resolveColor('var(--text)')
  const border = resolveColor('var(--border)')
  const accent = resolveColor('var(--accent)')
  const content: string[] = []
  const text = (value: string, tx: number, ty: number, size = 12) =>
    `<text x="${tx}" y="${ty}" font-family="${escapeXml(font)}" font-size="${size}" fill="${color}" dominant-baseline="central">${escapeXml(value)}</text>`
  content.push(text('Graph legend', x + 24, y + 28, 16))
  const columns = Math.max(1, Math.min(3, Math.floor((width - 48) / 340)))
  const columnWidth = (width - 48) / columns
  let rowY = y + 58
  let rowHeight = 0
  GRAPH_LEGEND_ITEMS.forEach((item, i) => {
    const column = i % columns
    if (i && column === 0) {
      rowY += rowHeight + 14
      rowHeight = 0
    }
    const left = x + 24 + column * columnWidth
    const lines = wrapLabel(item.label, columnWidth - 54, (value) => measure(value, 12))
    rowHeight = Math.max(rowHeight, lines.length * 18)
    if (item.kind === 'focus' || item.kind === 'group') {
      content.push(
        `<rect x="${left}" y="${rowY - 7}" width="26" height="14" rx="2" fill="${item.kind === 'focus' ? resolveColor('var(--selected)') : resolveColor('var(--surface-alt)')}" stroke="${border}" stroke-width="2"${item.kind === 'group' ? ' stroke-dasharray="4 3"' : ''}/>`,
      )
    } else {
      const stroke = item.kind === 'columns' || item.kind === 'arrow' ? accent : border
      content.push(
        `<path d="M${left},${rowY} h28" fill="none" stroke="${stroke}" stroke-width="${item.kind === 'columns' ? 2 : 1}"${item.kind === 'dynamic' ? ' stroke-dasharray="4 3"' : ''}/>`,
      )
      if (item.kind === 'arrow')
        content.push(
          `<path d="M${left + 23},${rowY - 4} l5,4 -5,4" fill="none" stroke="${stroke}" stroke-width="1.5"/>`,
        )
    }
    lines.forEach((line, lineIndex) => content.push(text(line, left + 40, rowY + lineIndex * 18)))
  })
  rowY += rowHeight + 16
  const note = wrapLabel(GRAPH_LEGEND_NOTE, width - 48, (value) => measure(value, 11))
  note.forEach((line, i) => content.push(text(line, x + 24, rowY + i * 16, 11)))
  rowY += note.length * 16 + 18
  let typeX = x + 24
  for (const type of types) {
    const lines = wrapLabel(type, width - 100, (value) => measure(value, 12))
    const itemWidth = Math.max(...lines.map((line) => measure(line, 12))) + 56
    if (typeX > x + 24 && typeX + itemWidth > x + width - 24) {
      typeX = x + 24
      rowY += 28
    }
    content.push(
      `<rect x="${typeX}" y="${rowY - 7}" width="26" height="14" rx="2" fill="none" stroke="${colorForType(type)}" stroke-width="2"/>`,
    )
    lines.forEach((line, i) => content.push(text(line, typeX + 36, rowY + i * 18)))
    if (lines.length > 1) {
      rowY += (lines.length - 1) * 18
      typeX = x + width
    } else typeX += itemWidth
  }
  const height = rowY - y + 28
  return {
    height,
    markup: `<g id="graph-legend" aria-label="Graph legend"><rect x="${x}" y="${y}" width="${width}" height="${height}" fill="${resolveColor('var(--surface)')}"/><path d="M${x},${y} h${width}" stroke="${border}"/>${content.join('')}</g>`,
  }
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
  const nodes = collectExportNodes(rawNodes.filter((node) => !node.hidden))
  const edges = collectExportEdges(rawEdges.filter((edge) => !edge.hidden))
  const dark = document.documentElement.dataset.theme === 'dark'
  const background = dark ? '#141414' : resolveColor('var(--surface)')
  const font =
    getComputedStyle(document.documentElement).getPropertyValue('--font-body').trim() ||
    'Segoe UI, Helvetica, Arial, sans-serif'
  const context = document.createElement('canvas').getContext('2d')
  const measure = (value: string, size: number) => {
    if (!context) return value.length * size * 0.6
    context.font = `${size}px ${font}`
    return context.measureText(value).width
  }

  if (nodes.length === 0) {
    const markup = `<svg xmlns="http://www.w3.org/2000/svg" width="240" height="80" viewBox="0 0 240 80"><rect width="240" height="80" fill="${background}"/><text x="12" y="44" font-family="ui-monospace, monospace" font-size="13">No nodes to export.</text></svg>`
    const legend = buildLegend(0, 80, 640, font, [], measure)
    const height = 80 + legend.height
    return {
      markup: markup
        .replace(
          'width="240" height="80" viewBox="0 0 240 80"',
          `width="640" height="${height}" viewBox="0 0 640 ${height}"`,
        )
        .replace('</svg>', `${legend.markup}</svg>`),
      width: 640,
      height,
    }
  }

  const bounds = nodes.map((n) => ({ x: n.x, y: n.y, width: n.width, height: n.height }))

  const byId = new Map(nodes.map((n) => [n.id, n]))
  const uniqueStrokes = [...new Set(edges.map((e) => e.stroke))]
  const markerDefs = uniqueStrokes
    .map(
      (c) =>
        `<marker id="${colorMarkerId(c)}" viewBox="-10 -10 20 20" refX="0" refY="0" markerWidth="20" markerHeight="20" orient="auto"><path d="M-5,-4 L0,0 L-5,4 z" fill="${c}" /></marker>`,
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
      const [path, midX, midY] = getBezierPath({
        sourceX: x1,
        sourceY: y1,
        targetX: x2,
        targetY: y2,
        sourcePosition: Position.Right,
        targetPosition: Position.Left,
      })
      // Include Bezier control points so reverse edges and cycles are not clipped.
      const points = path.match(/-?\d+(?:\.\d+)?(?:e[+-]?\d+)?/gi)?.map(Number) ?? []
      for (let i = 0; i < points.length; i += 2) bounds.push({ x: points[i], y: points[i + 1], width: 0, height: 0 })
      const labelWidth = measure(e.label ?? '', 10) + 6
      if (e.label) bounds.push({ x: midX - labelWidth / 2, y: midY - 9, width: labelWidth, height: 18 })
      const label = e.label
        ? `<rect x="${midX - labelWidth / 2}" y="${midY - 7}" width="${labelWidth}" height="14" rx="2" fill="${e.labelBackground}"/><text x="${midX}" y="${midY}" font-family="${escapeXml(font)}" font-size="10" fill="${e.labelColor}" text-anchor="middle" dominant-baseline="central">${escapeXml(e.label)}</text>`
        : ''
      return `<path d="${path}" fill="none" stroke="${e.stroke}" stroke-width="${e.strokeWidth}"${e.dashed ? ` stroke-dasharray="${escapeXml(e.dashed)}"` : ''} marker-end="url(#${colorMarkerId(e.stroke)})" />${label}`
    })
    .join('\n')

  const nodeMarkup = nodes
    .map((n) => {
      const lines = wrapLabel(n.label, n.width - 20, (value) => measure(value, n.fontSize))
      const text = lines
        .map(
          (line, i) =>
            `<text x="${n.x + n.width / 2}" y="${n.y + n.height / 2 + (i - (lines.length - 1) / 2) * n.lineHeight}" font-family="${escapeXml(font)}" font-size="${n.fontSize}" fill="${n.textColor}" text-anchor="middle" dominant-baseline="central">${escapeXml(line)}</text>`,
        )
        .join('')
      return `<g><title>${escapeXml(n.label)}</title>
        ${n.focus ? `<rect x="${n.x - 3}" y="${n.y - 3}" width="${n.width + 6}" height="${n.height + 6}" rx="5" fill="${resolveColor('var(--selected)')}"/>` : ''}
        <rect x="${n.x.toFixed(1)}" y="${n.y.toFixed(1)}" width="${n.width}" height="${n.height}" rx="2" fill="${n.background}" stroke="${n.border}" stroke-width="2"${n.dashed ? ' stroke-dasharray="6 4"' : ''} />
        ${text}
        <circle cx="${n.x}" cy="${n.y + n.height / 2}" r="3" fill="${dark ? '#bebebe' : '#1a192b'}" stroke="${dark ? '#1e1e1e' : '#fff'}"/>
        <circle cx="${n.x + n.width}" cy="${n.y + n.height / 2}" r="3" fill="${dark ? '#bebebe' : '#1a192b'}" stroke="${dark ? '#1e1e1e' : '#fff'}"/>
      </g>`
    })
    .join('\n')

  const graphMinX = Math.min(...bounds.map((b) => b.x)) - PADDING
  const minY = Math.min(...bounds.map((b) => b.y)) - PADDING
  const graphWidth = Math.ceil(Math.max(...bounds.map((b) => b.x + b.width)) + PADDING - graphMinX)
  const width = Math.max(640, graphWidth)
  const minX = graphMinX - (width - graphWidth) / 2
  const graphHeight = Math.ceil(Math.max(...bounds.map((b) => b.y + b.height)) + PADDING - minY)
  const types = [
    ...new Set(
      rawNodes
        .filter((node) => !node.hidden)
        .map((node) => node.data.objectType)
        .filter((type): type is string => typeof type === 'string'),
    ),
  ].sort()
  const legend = buildLegend(minX, minY + graphHeight, width, font, types, measure)
  const height = graphHeight + legend.height
  const markup = `<svg xmlns="http://www.w3.org/2000/svg" viewBox="${minX} ${minY} ${width} ${height}" width="${width}" height="${height}">
    <defs>${markerDefs}<pattern id="dots" width="20" height="20" patternUnits="userSpaceOnUse"><circle cx="10" cy="10" r="0.5" fill="${dark ? '#555' : '#91919a'}"/></pattern></defs>
    <rect x="${minX.toFixed(1)}" y="${minY.toFixed(1)}" width="${width.toFixed(1)}" height="${height.toFixed(1)}" fill="${background}" />
    <rect x="${minX}" y="${minY}" width="${width}" height="${height}" fill="url(#dots)"/>
    ${edgeMarkup}
    ${nodeMarkup}
    ${legend.markup}
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
  setTimeout(() => URL.revokeObjectURL(url), 1000)
}

export function downloadSvg(svg: GraphSvg, filename: string) {
  triggerDownload(new Blob([svg.markup], { type: 'image/svg+xml;charset=utf-8' }), filename)
}

/** High resolution for normal graphs, bounded by canvas dimensions and memory. */
export function pngDimensions(width: number, height: number, scale = 3) {
  const ratio = Math.min(scale, 16384 / width, 16384 / height, Math.sqrt(32_000_000 / (width * height)))
  return { width: Math.max(1, Math.floor(width * ratio)), height: Math.max(1, Math.floor(height * ratio)) }
}

/** Paint labels with the page's loaded fonts, which isolated SVG images cannot load. */
export async function downloadPng(svg: GraphSvg, filename: string) {
  await document.fonts?.ready
  const xml = new DOMParser().parseFromString(svg.markup, 'image/svg+xml')
  const labels = [...xml.querySelectorAll('text')].map((text) => {
    const label = {
      x: Number(text.getAttribute('x')),
      y: Number(text.getAttribute('y')),
      size: Number(text.getAttribute('font-size')),
      font: text.getAttribute('font-family')!,
      color: text.getAttribute('fill') || '#302d28',
      value: text.textContent ?? '',
      align: text.getAttribute('text-anchor') === 'middle' ? ('center' as const) : ('left' as const),
    }
    text.remove()
    return label
  })
  const [minX, minY] = xml.documentElement.getAttribute('viewBox')!.split(/\s+/).map(Number)
  const svgBlob = new Blob([new XMLSerializer().serializeToString(xml)], { type: 'image/svg+xml;charset=utf-8' })
  const url = URL.createObjectURL(svgBlob)
  try {
    const img = new Image()
    await new Promise<void>((resolve, reject) => {
      img.onload = () => resolve()
      img.onerror = () => reject(new Error('Unable to render the graph image.'))
      img.src = url
    })
    const canvas = document.createElement('canvas')
    const size = pngDimensions(svg.width, svg.height)
    canvas.width = size.width
    canvas.height = size.height
    const ctx = canvas.getContext('2d')
    if (!ctx) throw new Error('Unable to create the graph image.')
    ctx.scale(size.width / svg.width, size.height / svg.height)
    ctx.drawImage(img, 0, 0, svg.width, svg.height)
    ctx.translate(-minX, -minY)
    ctx.textBaseline = 'middle'
    for (const label of labels) {
      ctx.font = `${label.size}px ${label.font}`
      ctx.fillStyle = label.color
      ctx.textAlign = label.align
      ctx.fillText(label.value, label.x, label.y)
    }
    const blob = await new Promise<Blob | null>((resolve) => canvas.toBlob(resolve, 'image/png'))
    if (!blob) throw new Error('Unable to encode the graph image.')
    triggerDownload(blob, filename)
  } finally {
    URL.revokeObjectURL(url)
  }
}
