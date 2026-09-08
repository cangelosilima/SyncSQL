import { downloadBlob, exportFileName, type CsvColumn, type CsvValue } from './csv'

/**
 * Writes a multi-worksheet .xlsx download.
 *
 * Sheets are built from the same `CsvColumn` definitions the CSV exports use
 * (see lib/catalogCsv.ts), so the workbook and the CSV of the same data cannot
 * drift apart - one place still decides what a column is called and where its
 * value comes from.
 *
 * One deliberate difference from CSV: the leading-apostrophe formula guard in
 * `escapeCsvCell` is NOT applied here. That guard exists because a CSV cell
 * beginning "=" is parsed as a formula on open; ExcelJS writes a string as a
 * typed inline string, which Excel never evaluates, so applying the guard would
 * corrupt the data it was meant to protect.
 */

export interface SheetSpec {
  name: string
  headers: string[]
  /** Values already pulled through the column definitions - the data is in memory anyway. */
  rows: CsvValue[][]
}

/** Excel's own cap; a longer string makes a file Excel refuses to open. */
const MAX_CELL_LENGTH = 32767

/** Excel's worksheet-name limit. */
const MAX_SHEET_NAME_LENGTH = 31

/** Characters Excel forbids in a worksheet name. */
const ILLEGAL_SHEET_NAME_CHARS = /[:\\/?*[\]]/g

/**
 * Names Excel reserves for itself and refuses to accept - "History" is taken by
 * shared-workbook change tracking. Nothing about the name looks special, so this
 * is the kind of thing you only discover by writing a real file; the guard is
 * here so a future sheet can't rediscover it.
 */
const RESERVED_SHEET_NAMES = ['history']

/** Wide enough to read, narrow enough that a DDL column doesn't push everything else off screen. */
const MIN_COLUMN_WIDTH = 10
const MAX_COLUMN_WIDTH = 60

/**
 * Builds one worksheet from rows and the column definitions that describe them.
 * Generic per call, so each sheet keeps its own row type while the resulting
 * specs sit together in one array.
 */
export function sheet<T>(name: string, rows: readonly T[], columns: readonly CsvColumn<T>[]): SheetSpec {
  return {
    name,
    headers: columns.map((column) => column.header),
    rows: rows.map((row) => columns.map((column) => column.value(row))),
  }
}

/** A sheet name Excel will accept, unique within `taken` (which this adds to). */
export function sanitizeSheetName(name: string, taken: Set<string>): string {
  let cleaned = name.replace(ILLEGAL_SHEET_NAME_CHARS, ' ').trim() || 'Sheet'
  if (RESERVED_SHEET_NAMES.includes(cleaned.toLowerCase())) {
    cleaned = `${cleaned} (sheet)`
  }
  let candidate = cleaned.slice(0, MAX_SHEET_NAME_LENGTH)

  // Excel compares sheet names case-insensitively, so uniqueness has to as well.
  for (let n = 2; taken.has(candidate.toLowerCase()); n++) {
    const suffix = ` (${n})`
    candidate = `${cleaned.slice(0, MAX_SHEET_NAME_LENGTH - suffix.length)}${suffix}`
  }

  taken.add(candidate.toLowerCase())
  return candidate
}

/** Null and undefined become an empty cell; an over-long string is truncated rather than left to break the file. */
export function toCellValue(value: CsvValue): string | number | boolean | null {
  if (value === null || value === undefined) return null
  if (typeof value === 'string' && value.length > MAX_CELL_LENGTH) {
    return `${value.slice(0, MAX_CELL_LENGTH - 3)}...`
  }
  return value
}

/** Column width from the longest value in it, clamped so one long cell can't dominate the sheet. */
function columnWidth(header: string, values: CsvValue[]): number {
  const longest = values.reduce<number>(
    (max, value) => Math.max(max, value === null || value === undefined ? 0 : String(value).length),
    header.length,
  )
  return Math.min(Math.max(longest + 2, MIN_COLUMN_WIDTH), MAX_COLUMN_WIDTH)
}

export function xlsxFileName(...parts: (string | null | undefined)[]): string {
  return exportFileName('xlsx', ...parts)
}

/**
 * Renders the sheets to a workbook and hands it to the browser.
 *
 * ExcelJS is imported lazily so its ~1MB stays out of the initial bundle - this
 * is a button most visits never press, and the site is served as a static
 * artifact where first paint matters more than the first click of an export.
 */
export async function downloadXlsx(sheets: readonly SheetSpec[], filename: string): Promise<void> {
  const { Workbook } = await import('exceljs')
  const workbook = new Workbook()
  workbook.creator = 'SQLineage'
  workbook.created = new Date()

  const taken = new Set<string>()
  for (const spec of sheets) {
    const worksheet = workbook.addWorksheet(sanitizeSheetName(spec.name, taken))

    worksheet.columns = spec.headers.map((header, i) => ({
      header,
      key: `c${i}`,
      width: columnWidth(
        header,
        spec.rows.map((row) => row[i]),
      ),
    }))

    for (const row of spec.rows) {
      worksheet.addRow(row.map(toCellValue))
    }

    // A header that stays put and filters that are already on: the two things
    // anyone does by hand to a sheet like this before they can read it.
    worksheet.getRow(1).font = { bold: true }
    worksheet.views = [{ state: 'frozen', ySplit: 1 }]
    if (spec.headers.length > 0 && spec.rows.length > 0) {
      worksheet.autoFilter = { from: { row: 1, column: 1 }, to: { row: 1, column: spec.headers.length } }
    }
  }

  const buffer = await workbook.xlsx.writeBuffer()
  downloadBlob(
    new Blob([buffer], { type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet' }),
    filename,
  )
}
