import { downloadCsv, toCsv, type CsvColumn } from '../lib/csv'

interface CsvExportButtonProps<T> {
  rows: readonly T[]
  columns: readonly CsvColumn<T>[]
  /** Download file name, extension included - build it with csvFileName(). */
  filename: string
  label?: string
  /** Overrides the button's tooltip; defaults to naming the file and its row count. */
  title?: string
  className?: string
}

/**
 * Writes the rows it is given to a CSV download. Deliberately takes already
 * resolved rows rather than an id list: every caller renders the same rows on
 * screen, so the file and the table can't drift, and what you export is exactly
 * what the current filter left you looking at.
 */
export default function CsvExportButton<T>({
  rows,
  columns,
  filename,
  label = 'Export CSV',
  title,
  className = 'lineage-export-btn',
}: CsvExportButtonProps<T>) {
  const disabled = rows.length === 0
  return (
    <button
      type="button"
      className={className}
      disabled={disabled}
      title={disabled ? 'Nothing to export' : (title ?? `Download ${rows.length} row(s) as ${filename}`)}
      onClick={() => downloadCsv(toCsv(rows, columns), filename)}
    >
      {label}
    </button>
  )
}
