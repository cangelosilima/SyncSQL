import type { CSSProperties } from 'react'
import type { CatalogCommit } from '../types'
import { intensity } from '../lib/analytics'

const WINDOW_DAYS = 70

interface Cell {
  key: string
  date: Date
  count: number
  future: boolean
}

/** GitHub-style contribution calendar over the mined commit history, grouped by day. */
export default function ChangeActivityHeatmap({ commits }: { commits: CatalogCommit[] }) {
  const today = new Date()
  today.setHours(0, 0, 0, 0)

  const counts = new Map<string, number>()
  for (const commit of commits) {
    const d = new Date(commit.date)
    if (Number.isNaN(d.getTime())) continue
    d.setHours(0, 0, 0, 0)
    const key = dayKey(d)
    counts.set(key, (counts.get(key) ?? 0) + 1)
  }

  const start = new Date(today)
  start.setDate(start.getDate() - (WINDOW_DAYS - 1))
  start.setDate(start.getDate() - start.getDay()) // pad back to the preceding Sunday

  const totalDays = Math.round((today.getTime() - start.getTime()) / 86400000) + 1
  const weeks = Math.ceil(totalDays / 7)

  const cells: Cell[] = []
  for (let i = 0; i < weeks * 7; i++) {
    const d = new Date(start)
    d.setDate(d.getDate() + i)
    cells.push({ key: dayKey(d), date: d, count: counts.get(dayKey(d)) ?? 0, future: d > today })
  }

  const max = Math.max(1, ...counts.values())

  return (
    <div className="cal-heatmap">
      <div className="cal-heatmap-grid">
        {cells.map((cell) =>
          cell.future ? (
            <span key={cell.key} className="cal-cell" style={{ visibility: 'hidden' }} />
          ) : (
            <span
              key={cell.key}
              className="cal-cell"
              title={`${cell.date.toLocaleDateString()}: ${cell.count} commit${cell.count === 1 ? '' : 's'}`}
              style={cell.count > 0 ? cellStyle(intensity(cell.count, max)) : undefined}
            />
          ),
        )}
      </div>
      <div className="cal-heatmap-legend">
        less
        <span className="cal-cell" />
        <span className="cal-cell" style={cellStyle(0.3)} />
        <span className="cal-cell" style={cellStyle(0.65)} />
        <span className="cal-cell" style={cellStyle(1)} />
        more
      </div>
    </div>
  )
}

function cellStyle(level: number): CSSProperties {
  return {
    background: `color-mix(in srgb, var(--accent) ${Math.round(level * 100)}%, var(--surface-alt))`,
    borderColor: 'var(--accent)',
  }
}

function dayKey(d: Date): string {
  return d.toISOString().slice(0, 10)
}
