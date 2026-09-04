export type CsvValue = string | number | boolean | null | undefined

export interface CsvColumn<T> {
  header: string
  value: (row: T) => CsvValue
}

/** RFC 4180 line ending. Excel and every mainstream parser accept it; plain LF trips some older Excel builds. */
const ROW_SEPARATOR = '\r\n'

/**
 * A leading character Excel/Sheets would read as the start of a formula rather
 * than as text. Cell content here comes straight out of somebody's database
 * (object names, MS_Description text, grantee names), so a description starting
 * with "=" must not become a live formula the moment the file is opened.
 */
const FORMULA_LEAD = /^[=+@\t\r]/

/** A leading "-" is only dangerous when it isn't just a negative number, so real numeric text stays untouched. */
function needsFormulaGuard(text: string): boolean {
  if (FORMULA_LEAD.test(text)) return true
  return text.startsWith('-') && Number.isNaN(Number(text))
}

export function escapeCsvCell(value: CsvValue, delimiter = ','): string {
  if (value === null || value === undefined) return ''
  let text = typeof value === 'string' ? value : String(value)
  if (typeof value === 'string' && needsFormulaGuard(text)) {
    text = `'${text}`
  }
  // Newlines are kept (a multi-line description stays one cell) - quoting is
  // what makes that legal, so the check has to include them.
  if (text.includes(delimiter) || text.includes('"') || text.includes('\n') || text.includes('\r')) {
    return `"${text.split('"').join('""')}"`
  }
  return text
}

/** Serializes rows to CSV text: one header line from the column definitions, then one line per row. */
export function toCsv<T>(rows: readonly T[], columns: readonly CsvColumn<T>[], delimiter = ','): string {
  const lines = [columns.map((column) => escapeCsvCell(column.header, delimiter)).join(delimiter)]
  for (const row of rows) {
    lines.push(columns.map((column) => escapeCsvCell(column.value(row), delimiter)).join(delimiter))
  }
  return lines.join(ROW_SEPARATOR) + ROW_SEPARATOR
}

/** Turns an object id (or any label) into a filename-safe stem, so "SQLPROD01/AppDb/Tables/dbo/Orders" downloads as something a filesystem accepts. */
export function csvFileName(...parts: (string | null | undefined)[]): string {
  const stem = parts
    .filter((part): part is string => Boolean(part && part.trim()))
    .join('-')
    .replace(/[^A-Za-z0-9._-]+/g, '-')
    .replace(/-{2,}/g, '-')
    .replace(/^-|-$/g, '')
  return `${stem || 'syncsql-export'}.csv`
}

/**
 * Saves CSV text as a download. The UTF-8 BOM is deliberate: without it Excel
 * reads the file in the machine's ANSI codepage and mangles every accented
 * object name and description - the exact content this export exists to carry.
 */
export function downloadCsv(csv: string, filename: string): void {
  const blob = new Blob([`\uFEFF${csv}`], { type: 'text/csv;charset=utf-8' })
  const url = URL.createObjectURL(blob)
  const anchor = document.createElement('a')
  anchor.href = url
  anchor.download = filename
  document.body.appendChild(anchor)
  anchor.click()
  document.body.removeChild(anchor)
  URL.revokeObjectURL(url)
}
