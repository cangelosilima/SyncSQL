// @vitest-environment node
import { createRequire } from 'node:module'
import { mkdtempSync, readFileSync, rmSync } from 'node:fs'
import { tmpdir } from 'node:os'
import path from 'node:path'
import { describe, expect, it } from 'vitest'

const require = createRequire(import.meta.url)

// Exercise the parent packages' API usage when security overrides cross their
// declared version ranges. Keep these checks until upstream adopts the fixes.
describe('security override compatibility', () => {
  it('lets ExcelJS generate and reload extended conditional formatting with uuid 11', async () => {
    const ExcelJS = require('exceljs')
    const workbook = new ExcelJS.Workbook()
    const sheet = workbook.addWorksheet('Values')
    sheet.addRows([[1], [2], [3]])
    sheet.addConditionalFormatting({
      ref: 'A1:A3',
      rules: [{ type: 'iconSet', iconSet: '3Stars', cfvo: [
        { type: 'percent', value: 0 }, { type: 'percent', value: 33 }, { type: 'percent', value: 67 },
      ] }],
    })
    const bytes = await workbook.xlsx.writeBuffer()
    const reloaded = new ExcelJS.Workbook()
    await reloaded.xlsx.load(bytes)
    expect(reloaded.getWorksheet('Values').getCell('A2').value).toBe(2)
    expect(sheet.conditionalFormattings[0].rules[0].x14Id).toMatch(/^\{[0-9A-F-]{36}\}$/)
  })

  it('supports the ONNX installer ZIP entry extraction API with adm-zip 0.6', () => {
    const onnxRequire = createRequire(require.resolve('onnxruntime-node'))
    const AdmZip = onnxRequire('adm-zip')
    const zip = new AdmZip()
    zip.addFile('runtimes/native/sample.dll', Buffer.from('fixture'))
    const reloaded = new AdmZip(zip.toBuffer())
    const directory = mkdtempSync(path.join(tmpdir(), 'syncsql-zip-'))
    try {
      reloaded.extractEntryTo(reloaded.getEntry('runtimes/native/sample.dll'), directory, false, true)
      expect(readFileSync(path.join(directory, 'sample.dll'), 'utf8')).toBe('fixture')
    } finally {
      rmSync(directory, { recursive: true, force: true })
    }
  })

  it('resizes Transformers raw images through sharp 0.35', async () => {
    const { RawImage } = await import('@huggingface/transformers')
    const image = new RawImage(new Uint8ClampedArray([255, 0, 0]), 1, 1, 3)
    const resized = await image.resize(2, 2)
    expect([resized.width, resized.height, resized.channels]).toEqual([2, 2, 3])
    expect(Array.from(resized.data.slice(0, 3))).toEqual([255, 0, 0])
  })
})
