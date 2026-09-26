import { fireEvent, render, screen } from '@testing-library/react'
import { afterEach, expect, it, vi } from 'vitest'
import ContentSearchBar from './ContentSearchBar'
import CatalogLoadStatus from './CatalogLoadStatus'
import DiffView from './DiffView'
import TrendChart from './TrendChart'
import MetricsPanels from './MetricsPanels'
import TypeActivityHeatmap from './TypeActivityHeatmap'
import type { CatalogMetricSnapshot } from '../types'

afterEach(() => {
  vi.useRealTimers()
  vi.unstubAllGlobals()
})

it('searches, clears, and shows singular and plural match counts only for a nonempty query', () => {
  const onChange = vi.fn()
  const { rerender } = render(<ContentSearchBar value="" onChange={onChange} />)
  expect(screen.queryByRole('button')).not.toBeInTheDocument()
  fireEvent.change(screen.getByRole('textbox'), { target: { value: 'Orders' } })
  expect(onChange).toHaveBeenCalledWith('Orders')
  rerender(<ContentSearchBar value="Orders" onChange={onChange} matchCount={1} placeholder="Find SQL" />)
  expect(screen.getByText('1 match')).toBeVisible()
  expect(screen.getByPlaceholderText('Find SQL')).toBeVisible()
  fireEvent.click(screen.getByRole('button', { name: 'Clear DDL search' }))
  expect(onChange).toHaveBeenCalledWith('')
  rerender(<ContentSearchBar value="Orders" onChange={onChange} matchCount={2} />)
  expect(screen.getByText('2 matches')).toBeVisible()
  rerender(<ContentSearchBar value="Orders" onChange={onChange} />)
  expect(screen.queryByText(/matches/)).not.toBeInTheDocument()
})

it('shows catalog loading and failures, with a working reload action', () => {
  const { rerender, container } = render(<CatalogLoadStatus loading={false} />)
  expect(container).toBeEmptyDOMElement()
  rerender(<CatalogLoadStatus loading />)
  expect(screen.getByRole('status')).toHaveTextContent('Loading')
  rerender(<CatalogLoadStatus loading={false} error="Offline" />)
  expect(screen.getByRole('alert')).toHaveTextContent('Offline')
  const reload = vi.fn()
  const original = window
  vi.stubGlobal(
    'window',
    new Proxy(original, {
      get: (target, property) => (property === 'location' ? { reload } : Reflect.get(target, property)),
    }),
  )
  fireEvent.click(screen.getByRole('button', { name: 'Reload catalog' }))
  expect(reload).toHaveBeenCalledOnce()
})

it('renders equal, inserted and deleted SQL lines and guards oversized revisions', () => {
  const { rerender, container } = render(
    <DiffView oldText={'same\nold'} newText={'same\nnew'} oldLabel="Before" newLabel="After" />,
  )
  expect(screen.getByText('Before')).toBeVisible()
  expect(container.querySelector('.diff-cell--del')).toHaveTextContent('old')
  expect(container.querySelector('.diff-cell--ins')).toHaveTextContent('new')
  expect(container.querySelectorAll('.diff-cell--blank')).toHaveLength(2)
  rerender(<DiffView oldText="same" newText="same" oldLabel="Before" newLabel="After" />)
  expect(screen.getByText(/No differences/)).toBeVisible()
  rerender(<DiffView oldText={'line\n'.repeat(2001)} newText="new" oldLabel="Before" newLabel="After" />)
  expect(screen.getByText(/too large/)).toBeVisible()
})

it('plots gaps, negative and zero values, multiple series and single-point charts', () => {
  const { rerender, container } = render(<TrendChart labels={[]} series={[]} />)
  expect(screen.getByText('No data points yet.')).toBeVisible()
  rerender(
    <TrendChart
      labels={['a', 'b', 'c', 'd']}
      series={[
        { name: 'Rows', color: 'red', values: [-1, 1, null, 2] },
        { name: 'Missing', color: 'blue', values: [null, undefined as unknown as null] },
      ]}
    />,
  )
  expect(container.querySelector('path')?.getAttribute('d')).toMatch(/^M.*L.*M/)
  expect(container.querySelectorAll('circle')).toHaveLength(3)
  expect(screen.getByText('a - Rows: -1')).toBeInTheDocument()
  expect(screen.getByText('a → d')).toBeVisible()
  rerender(
    <TrendChart
      labels={['a']}
      series={[{ name: 'Rows', color: 'red', values: [0] }]}
      height={100}
      formatValue={(n) => `${n} rows`}
    />,
  )
  expect(container.querySelector('circle')).toHaveAttribute('cx', '320')
  expect(screen.getByText('a - Rows: 0 rows')).toBeInTheDocument()
  rerender(<TrendChart labels={[]} series={[{ name: 'Rows', color: 'red', values: [1] }]} />)
  expect(container.querySelector('.trend-chart-range')).toBeNull()
})

const snapshot = (overrides: Partial<CatalogMetricSnapshot> = {}): CatalogMetricSnapshot => ({
  capturedAt: '2026-01-01',
  rowCount: null,
  reservedKB: null,
  dataKB: null,
  indexKB: null,
  indexes: [],
  statistics: [],
  ...overrides,
})

it('shows metric history, switches SQL Server and Oracle indexes, and falls back when an index disappears', () => {
  const { rerender } = render(<MetricsPanels metrics={[]} />)
  expect(screen.getByRole('heading', { name: 'Volume' })).toBeVisible()
  const metrics = [
    snapshot(),
    snapshot({
      rowCount: 2000,
      reservedKB: 1048576,
      dataKB: 1024,
      indexKB: 32,
      indexes: [
        { name: 'SQL', fragmentationPct: 2, seeks: 1, scans: 2, lookups: 3, updates: 4 },
        { name: 'Oracle', rowCount: 12, leafBlocks: 3 },
        { name: 'Unknown', fragmentationPct: null, rowCount: null },
      ],
      statistics: [
        { name: 'filled', rows: 20, rowsSampled: 10, steps: 2, modificationCounter: 3, lastUpdated: '2026-01-01' },
        { name: 'empty', rows: null, rowsSampled: null, steps: null, modificationCounter: null, lastUpdated: null },
      ],
    }),
  ]
  rerender(<MetricsPanels metrics={metrics} />)
  expect(screen.getByText('1.0 GB')).toBeVisible()
  expect(screen.getByText('Fragmentation %')).toBeVisible()
  expect(screen.getByRole('region', { name: 'Optimizer statistics table' })).toHaveTextContent('filled')
  fireEvent.click(screen.getByRole('button', { name: 'Oracle' }))
  expect(screen.getByText('Leaf blocks')).toBeVisible()
  fireEvent.click(screen.getByRole('button', { name: 'Unknown' }))
  expect(screen.queryByText('Leaf blocks')).not.toBeInTheDocument()
  rerender(
    <MetricsPanels metrics={[snapshot({ reservedKB: 1024, indexes: [{ name: 'Replacement', rowCount: 0 }] })]} />,
  )
  expect(screen.getByRole('button', { name: 'Replacement' })).toHaveClass('active')
  expect(screen.getByText('1.0 MB')).toBeVisible()
  rerender(<MetricsPanels metrics={[snapshot({ reservedKB: 12 })]} />)
  expect(screen.getByText('12 KB')).toBeVisible()
})

it('counts each touched type once per commit and ignores invalid, old, future and unknown objects', () => {
  vi.useFakeTimers()
  vi.setSystemTime(new Date('2026-09-23T12:00:00'))
  const commits = ['2026-09-22', '2026-09-21', 'bad', '2020-01-01', '2030-01-01'].map((date, i) => ({
    sha: String(i),
    date,
    message: 'change',
    author: 'Dev',
    objectIds: ['a', 'b', 'c', 'missing'],
  }))
  const { container, rerender } = render(
    <TypeActivityHeatmap
      commits={commits}
      typeById={
        new Map([
          ['a', 'Tables'],
          ['b', 'Tables'],
          ['c', 'Views'],
        ])
      }
      types={['Tables', 'Procedures']}
      weeks={2}
    />,
  )
  expect(container.querySelector('.type-heatmap-count')).toHaveTextContent('2')
  expect(screen.getByTitle(/2 Tables changes/)).toHaveAttribute('style')
  rerender(<TypeActivityHeatmap commits={[commits[0]]} typeById={new Map([['a', 'Tables']])} types={['Tables']} />)
  expect(screen.getByTitle(/1 Tables change$/)).toBeInTheDocument()
  expect(container.querySelectorAll('.type-heatmap-cell')).toHaveLength(30)
})
