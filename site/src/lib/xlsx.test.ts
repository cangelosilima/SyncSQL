import { describe, expect, it, vi } from 'vitest'
import { downloadXlsx, sanitizeSheetName, sheet, toCellValue, xlsxFileName } from './xlsx'
import type { CsvColumn } from './csv'

interface Row {
  name: string
  size: number | null
}

const columns: CsvColumn<Row>[] = [
  { header: 'Name', value: (r) => r.name },
  { header: 'Size', value: (r) => r.size },
]

describe('sheet', () => {
  it('resolves headers and values through the shared column definitions', () => {
    const spec = sheet('Objects', [{ name: 'dbo.Orders', size: 12 }], columns)

    expect(spec.name).toBe('Objects')
    expect(spec.headers).toEqual(['Name', 'Size'])
    expect(spec.rows).toEqual([['dbo.Orders', 12]])
  })

  it('keeps an empty row set rather than inventing one', () => {
    expect(sheet('Empty', [], columns).rows).toEqual([])
  })
})

describe('sanitizeSheetName', () => {
  it('strips the characters Excel forbids', () => {
    expect(sanitizeSheetName('a/b:c?d*e[f]g', new Set())).toBe('a b c d e f g')
  })

  it('truncates to Excel’s 31-character limit', () => {
    expect(sanitizeSheetName('x'.repeat(40), new Set())).toHaveLength(31)
  })

  it('de-duplicates case-insensitively, as Excel compares them', () => {
    const taken = new Set<string>()

    expect(sanitizeSheetName('Details', taken)).toBe('Details')
    expect(sanitizeSheetName('details', taken)).toBe('details (2)')
    expect(sanitizeSheetName('DETAILS', taken)).toBe('DETAILS (3)')
  })

  it('keeps a de-duplicated name inside the length limit', () => {
    const taken = new Set<string>()
    sanitizeSheetName('y'.repeat(40), taken)

    expect(sanitizeSheetName('y'.repeat(40), taken).length).toBeLessThanOrEqual(31)
  })

  it('falls back to a usable name when everything is stripped', () => {
    expect(sanitizeSheetName('///', new Set())).toBe('Sheet')
  })
})

describe('toCellValue', () => {
  /**
   * The CSV writer prefixes an apostrophe so a spreadsheet can't read "=..." as a
   * formula. ExcelJS writes strings as typed inline values, which Excel never
   * evaluates, so doing the same here would corrupt the data instead.
   */
  it('does not apply the CSV formula guard', () => {
    expect(toCellValue('=SUM(A1:A2)')).toBe('=SUM(A1:A2)')
    expect(toCellValue('-notanumber')).toBe('-notanumber')
  })

  it('maps null and undefined to an empty cell', () => {
    expect(toCellValue(null)).toBeNull()
    expect(toCellValue(undefined)).toBeNull()
  })

  it('passes numbers and booleans through unchanged', () => {
    expect(toCellValue(42)).toBe(42)
    expect(toCellValue(false)).toBe(false)
  })

  it('truncates a string past the cell limit rather than writing a file Excel refuses to open', () => {
    const value = toCellValue('x'.repeat(40000))

    expect(typeof value).toBe('string')
    expect((value as string).length).toBe(32767)
    expect(value as string).toMatch(/\.\.\.$/)
  })
})

describe('xlsxFileName', () => {
  it('makes an object id filesystem-safe', () => {
    expect(xlsxFileName('SQLPROD01/AppDb/Tables/dbo/Orders')).toBe('SQLPROD01-AppDb-Tables-dbo-Orders.xlsx')
  })
})

function readBlob(blob: Blob): Promise<ArrayBuffer> {
  return new Promise((resolve, reject) => {
    const reader = new FileReader()
    reader.onload = () => resolve(reader.result as ArrayBuffer)
    reader.onerror = () => reject(reader.error)
    reader.readAsArrayBuffer(blob)
  })
}

describe('downloadXlsx', () => {
  it('writes a real workbook and hands it to the browser', async () => {
    const createObjectURL = vi.fn((_blob: Blob) => 'blob:fake')
    const revokeObjectURL = vi.fn()
    vi.stubGlobal('URL', { ...URL, createObjectURL, revokeObjectURL })
    const click = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => {})

    await downloadXlsx([sheet('Objects', [{ name: 'dbo.Orders', size: 12 }], columns)], 'objects.xlsx')

    expect(click).toHaveBeenCalled()
    const blob = createObjectURL.mock.calls[0][0]
    // A .xlsx is a zip; "PK" is its signature, so this checks a real file came
    // out rather than an empty or half-written one. Read through FileReader
    // because jsdom's Blob implements neither text() nor arrayBuffer().
    expect(blob.size).toBeGreaterThan(0)
    const head = new Uint8Array(await readBlob(blob)).slice(0, 2)
    expect([head[0], head[1]]).toEqual(['P'.charCodeAt(0), 'K'.charCodeAt(0)])

    click.mockRestore()
    vi.unstubAllGlobals()
  })
})
