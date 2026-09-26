import { afterEach, expect, it, vi } from 'vitest'
import { downloadCsv } from './csv'
import { downloadPng, downloadSvg } from './graphExport'
afterEach(() => {
  vi.restoreAllMocks()
  vi.unstubAllGlobals()
  vi.useRealTimers()
})
function downloads() {
  vi.useFakeTimers()
  const createObjectURL = vi.fn(() => 'blob:test')
  const revokeObjectURL = vi.fn()
  vi.stubGlobal('URL', { createObjectURL, revokeObjectURL })
  const click = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => {})
  return { createObjectURL, revokeObjectURL, click }
}
const svg = {
  markup:
    '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 100 100"><text x="0" y="0" font-size="12" font-family="Arial">Hello</text></svg>',
  width: 100,
  height: 100,
}
it('downloads CSV and SVG blobs and revokes their temporary URLs', () => {
  const { click, createObjectURL, revokeObjectURL } = downloads()
  downloadCsv('a,b', 'data.csv')
  downloadSvg(svg, 'graph.svg')
  expect(click).toHaveBeenCalledTimes(2)
  expect(createObjectURL.mock.calls.map(([blob]) => (blob as Blob).type)).toEqual([
    'text/csv;charset=utf-8',
    'image/svg+xml;charset=utf-8',
  ])
  vi.runAllTimers()
  expect(revokeObjectURL).toHaveBeenCalledTimes(2)
  expect(document.querySelector('a[download]')).toBeNull()
})
it.each(['success', 'image', 'canvas'])('handles PNG %s and always releases temporary resources', async (outcome) => {
  const { click, revokeObjectURL } = downloads()
  vi.stubGlobal(
    'Image',
    class {
      onload!: () => void
      onerror!: () => void
      set src(_value: string) {
        Promise.resolve().then(() => (outcome === 'image' ? this.onerror() : this.onload()))
      }
    },
  )
  const context = { scale: vi.fn(), drawImage: vi.fn(), translate: vi.fn(), fillText: vi.fn() }
  vi.spyOn(HTMLCanvasElement.prototype, 'getContext').mockReturnValue(
    outcome === 'canvas' ? null : (context as unknown as CanvasRenderingContext2D),
  )
  vi.spyOn(HTMLCanvasElement.prototype, 'toBlob').mockImplementation((callback) => callback(new Blob(['png'])))
  if (outcome === 'success') {
    await downloadPng(svg, 'graph.png')
    expect(click).toHaveBeenCalledOnce()
    expect(context.fillText).toHaveBeenCalledWith('Hello', 0, 0)
  } else await expect(downloadPng(svg, 'graph.png')).rejects.toThrow(outcome === 'image' ? 'render' : 'create')
  vi.runAllTimers()
  expect(revokeObjectURL).toHaveBeenCalled()
})
