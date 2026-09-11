import { act, cleanup, fireEvent, render, screen } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import CsvExportButton from './CsvExportButton'
import XlsxExportButton from './XlsxExportButton'
import { downloadCsv } from '../lib/csv'
import { downloadXlsx } from '../lib/xlsx'

vi.mock('../lib/csv', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../lib/csv')>()),
  downloadCsv: vi.fn(),
}))
vi.mock('../lib/xlsx', () => ({ downloadXlsx: vi.fn() }))

afterEach(() => {
  cleanup()
  vi.resetAllMocks()
})

describe('export action parity through shared Button', () => {
  const columns = [{ header: 'Object', value: (row: { name: string }) => row.name }]

  it('disables an empty CSV and retains its explanation', () => {
    render(<CsvExportButton rows={[]} columns={columns} filename="objects.csv" />)
    const button = screen.getByRole('button', { name: 'Export CSV' })
    expect(button).toBeDisabled()
    expect(button).toHaveAttribute('title', 'Nothing to export')
    fireEvent.click(button)
    expect(downloadCsv).not.toHaveBeenCalled()
  })

  it('exports every supplied match beyond the 500 visible-row boundary', () => {
    const rows = Array.from({ length: 501 }, (_, i) => ({ name: `dbo.Object${i}` }))
    render(<CsvExportButton rows={rows} columns={columns} filename="objects.csv" />)
    fireEvent.click(screen.getByRole('button', { name: 'Export CSV' }))
    expect(downloadCsv).toHaveBeenCalledOnce()
    const [csv, filename] = vi.mocked(downloadCsv).mock.calls[0]
    expect(filename).toBe('objects.csv')
    expect(csv).toContain('dbo.Object0')
    expect(csv).toContain('dbo.Object500')
    expect(csv.trim().split(/\r?\n/)).toHaveLength(502)
  })

  it('builds XLSX lazily, prevents repeat clicks while busy and allows retry after failure', async () => {
    let rejectExport!: (reason: Error) => void
    const pending = new Promise<void>((_, reject) => {
      rejectExport = reject
    })
    vi.mocked(downloadXlsx).mockReturnValueOnce(pending).mockResolvedValueOnce(undefined)
    const sheets = vi.fn(() => [{ name: 'Objects', headers: ['Object'], rows: [['dbo.Orders']] }])
    render(<XlsxExportButton sheets={sheets} filename="object.xlsx" />)
    expect(sheets).not.toHaveBeenCalled()
    fireEvent.click(screen.getByRole('button', { name: 'Export XLSX' }))
    const busy = screen.getByRole('button', { name: 'Exporting...' })
    expect(busy).toBeDisabled()
    expect(busy).toHaveAttribute('aria-busy', 'true')
    fireEvent.click(busy)
    expect(sheets).toHaveBeenCalledOnce()
    await act(async () => {
      rejectExport(new Error('Workbook failed'))
      await pending.catch(() => {})
    })
    expect(screen.getByRole('alert')).toHaveTextContent('Workbook failed')
    await act(async () => {
      fireEvent.click(screen.getByRole('button', { name: 'Export XLSX' }))
    })
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Export XLSX' })).toBeEnabled()
    expect(downloadXlsx).toHaveBeenCalledTimes(2)
    expect(downloadXlsx).toHaveBeenLastCalledWith(sheets.mock.results[1].value, 'object.xlsx')
  })
})
