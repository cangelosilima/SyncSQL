import { useState } from 'react'
import { downloadXlsx, type SheetSpec } from '../lib/xlsx'

interface XlsxExportButtonProps {
  /** Built lazily: the workbook is only assembled when somebody actually asks for it. */
  sheets: () => SheetSpec[]
  /** Download file name, extension included - build it with xlsxFileName(). */
  filename: string
  label?: string
  title?: string
  className?: string
}

/**
 * Mirrors CsvExportButton, but asynchronous: the workbook writer is a lazily
 * imported dependency, so there is a real gap between the click and the
 * download. The button says so rather than looking dead, and a failure is shown
 * next to it instead of vanishing into the console.
 *
 * `sheets` is a function rather than a value so building the workbook's rows -
 * which walks the object's whole neighbourhood - doesn't happen on every render
 * of a page whose export button may never be pressed.
 */
export default function XlsxExportButton({
  sheets,
  filename,
  label = 'Export XLSX',
  title,
  className = 'lineage-export-btn',
}: XlsxExportButtonProps) {
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  async function run() {
    setBusy(true)
    setError(null)
    try {
      await downloadXlsx(sheets(), filename)
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Export failed.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <>
      <button
        type="button"
        className={className}
        disabled={busy}
        title={title ?? `Download every section of this object as ${filename}`}
        onClick={run}
      >
        {busy ? 'Exporting...' : label}
      </button>
      {error && (
        <span className="muted" role="alert">
          {error}
        </span>
      )}
    </>
  )
}
