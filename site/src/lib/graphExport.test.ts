import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import type { Node } from '@xyflow/react'
import { buildLineageGraphSvg, downloadPng, pngDimensions } from './graphExport'

const context = { font: '', measureText: (text: string) => ({ width: text.length * 6 }), scale: vi.fn(), drawImage: vi.fn(), translate: vi.fn(), fillText: vi.fn() }
beforeEach(() => {
  vi.spyOn(HTMLCanvasElement.prototype, 'getContext').mockReturnValue(context as unknown as CanvasRenderingContext2D)
  document.documentElement.style.cssText = '--surface:#fffdf8;--surface-alt:#eee7dc;--selected:#dcebe2;--text:#302d28;--border:#918576;--accent:#28665c;--text-muted:#696052;--font-body:Source Sans 3, sans-serif'
})
afterEach(() => { vi.restoreAllMocks(); vi.unstubAllGlobals(); document.documentElement.removeAttribute('style'); document.documentElement.removeAttribute('data-theme') })

const nodes: Node[] = [
  { id: 'a', data: { label: 'SCHEMA.A_VERY_LONG_IDENTIFIER_WITH_NO_TRUNCATION<&>' }, position: { x: 0, y: 0 }, measured: { width: 220, height: 74 }, className: 'lineage-node--focus', style: { background: 'var(--selected)', border: '2px solid #ff9900' } },
  { id: 'b', data: { label: '8 grouped Tables' }, position: { x: 350, y: 180 }, style: { width: 220, height: 56, border: '2px dashed #4488ff' } },
]

it('preserves curved dynamic edges, type borders, focus, theme and complete wrapped names', () => {
  const svg = buildLineageGraphSvg(nodes, [{ id: 'ab', source: 'a', target: 'b', label: 'ID, AMOUNT', style: { stroke: '#28665c', strokeWidth: 1.5, strokeDasharray: '4 3' } }])
  const xml = new DOMParser().parseFromString(svg.markup, 'image/svg+xml')
  expect(xml.querySelector('parsererror')).toBeNull()
  const edge = xml.querySelector('path[marker-end]')!
  expect(edge.getAttribute('d')).toContain(' C')
  expect(edge.getAttribute('stroke-dasharray')).toBe('4 3')
  expect(edge.getAttribute('stroke-width')).toBe('1.5')
  expect(xml.querySelector('rect[stroke-dasharray="6 4"]')).not.toBeNull()
  expect(xml.querySelector('rect[rx="5"]')?.getAttribute('fill')).toBe('#dcebe2')
  const text = [...xml.querySelectorAll('g text')].map(el => el.textContent).join('')
  expect(text).toContain(nodes[0].data.label)
  expect(svg.markup).not.toContain('…')
  expect(svg.markup).toContain('Source Sans 3')
  expect(svg.markup).not.toContain('var(--')
  expect(xml.querySelector('pattern#dots')).not.toBeNull()
})

it('fits reverse Bezier control points and skips hidden nodes', () => {
  const svg = buildLineageGraphSvg([...nodes, { ...nodes[0], id: 'hidden', hidden: true, position: { x: 10000, y: 10000 } }], [{ id: 'ba', source: 'b', target: 'a' }])
  const xml = new DOMParser().parseFromString(svg.markup, 'image/svg+xml')
  const [x, y, width, height] = xml.documentElement.getAttribute('viewBox')!.split(' ').map(Number)
  const points = xml.querySelector('path[marker-end]')!.getAttribute('d')!.match(/-?\d+(?:\.\d+)?/g)!.map(Number)
  for (let i = 0; i < points.length; i += 2) {
    expect(points[i]).toBeGreaterThan(x); expect(points[i]).toBeLessThan(x + width)
    expect(points[i + 1]).toBeGreaterThan(y); expect(points[i + 1]).toBeLessThan(y + height)
  }
  expect(svg.width).toBeLessThan(2000)
})

it('renders the dark graph background and readable empty state', () => {
  document.documentElement.dataset.theme = 'dark'
  expect(buildLineageGraphSvg(nodes, []).markup).toContain('fill="#141414"')
  expect(buildLineageGraphSvg([], []).markup).toContain('No nodes to export.')
})

it.each([220, 1380])('always places a complete legend below the graph at width %s', width => {
  const svg = buildLineageGraphSvg([
    { ...nodes[0], measured: { width, height: 74 }, data: { label: 'Focus', objectType: 'Packages' } },
    { ...nodes[1], data: { label: '8 grouped Tables', objectType: 'Tables' } },
    { ...nodes[1], id: 'hidden', hidden: true, data: { label: 'Hidden', objectType: 'Views' } },
  ], [])
  const xml = new DOMParser().parseFromString(svg.markup, 'image/svg+xml')
  const legend = xml.querySelector('#graph-legend')!
  const text = [...legend.querySelectorAll('text')].map(el => el.textContent).join(' ')
  for (const label of ['Graph legend', 'Referencing object → referenced object', 'recorded column references', 'dynamic SQL reference (weaker evidence)', 'Current focus', 'grouped objects', 'not complete column lineage', 'Packages', 'Tables']) expect(text).toContain(label)
  expect(text).not.toContain('Views')
  expect(legend.querySelector('rect[stroke="#14b8a6"]')).not.toBeNull()
  expect(legend.querySelector('rect[stroke="#3b82f6"]')).not.toBeNull()
  const top = Number(legend.querySelector('rect')!.getAttribute('y'))
  expect(top).toBeGreaterThan(236)
  const [minX, minY] = xml.documentElement.getAttribute('viewBox')!.split(' ').map(Number)
  for (const label of legend.querySelectorAll('text')) {
    expect(Number(label.getAttribute('y'))).toBeLessThan(minY + svg.height)
    expect(Number(label.getAttribute('x')) + (label.textContent?.length ?? 0) * 6).toBeLessThan(minX + svg.width)
  }
  expect(buildLineageGraphSvg([], []).markup).toContain('Graph legend')
})

it('exports at 3x while bounding very wide and dense graphs', () => {
  expect(pngDimensions(1000, 500)).toEqual({ width: 3000, height: 1500 })
  for (const [width, height] of [[50000, 100], [5000, 5000], [100, 50000]]) {
    const size = pngDimensions(width, height)
    expect(size.width).toBeLessThanOrEqual(16384)
    expect(size.height).toBeLessThanOrEqual(16384)
    expect(size.width * size.height).toBeLessThanOrEqual(32_000_000)
  }
})

it('draws PNG labels with the loaded page font and cleans up failed encodes', async () => {
  vi.stubGlobal('Image', class { onload?: () => void; set src(_value: string) { queueMicrotask(() => this.onload?.()) } })
  const create = vi.fn(() => 'blob:graph'), revoke = vi.fn()
  vi.stubGlobal('URL', { createObjectURL: create, revokeObjectURL: revoke })
  vi.spyOn(HTMLCanvasElement.prototype, 'toBlob').mockImplementation(callback => callback(null))
  await expect(downloadPng(buildLineageGraphSvg(nodes, []), 'graph.png')).rejects.toThrow('Unable to encode')
  expect(context.fillText).toHaveBeenCalled()
  expect(context.fillText).toHaveBeenCalledWith('Graph legend', expect.any(Number), expect.any(Number))
  expect(context.font).toContain('Source Sans 3')
  expect(revoke).toHaveBeenCalledWith('blob:graph')
})
