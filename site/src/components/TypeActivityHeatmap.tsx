import type { CSSProperties } from 'react'
import type { CatalogCommit } from '../types'
import TypeBadge from './TypeBadge'
import { colorForType } from '../lib/typeColors'
import { intensity } from '../lib/analytics'

export const CHANGE_ACTIVITY_WEEKS = 30

/**
 * Change-activity heatmap broken out by object type: one row per type (same
 * set/order as the old "object breakdown" counts), one cell per week over
 * the mined history, each row tinted with that type's own color. Replaces
 * the separate object-breakdown bar list and single-color calendar heatmap
 * with one combined view.
 */
export default function TypeActivityHeatmap({
  commits,
  typeById,
  types,
  weeks = CHANGE_ACTIVITY_WEEKS,
}: {
  commits: CatalogCommit[]
  /** Node id -> object type, used to attribute each commit's touched objects to a row. */
  typeById: Map<string, string>
  /** Row order/set - typically catalog.typeCounts keys, most-frequent first. */
  types: string[]
  weeks?: number
}) {
  const today = new Date()
  today.setHours(0, 0, 0, 0)

  const start = new Date(today)
  start.setDate(start.getDate() - (weeks * 7 - 1))
  start.setDate(start.getDate() - start.getDay()) // pad back to the preceding Sunday

  const weekStarts: Date[] = []
  for (let w = 0; w < weeks; w++) {
    const d = new Date(start)
    d.setDate(d.getDate() + w * 7)
    weekStarts.push(d)
  }

  const matrix = new Map<string, number[]>(types.map((type) => [type, new Array(weeks).fill(0)]))

  for (const commit of commits) {
    const d = new Date(commit.date)
    if (Number.isNaN(d.getTime()) || d < start) continue
    d.setHours(0, 0, 0, 0)
    const weekIndex = Math.floor((d.getTime() - start.getTime()) / (7 * 86400000))
    if (weekIndex < 0 || weekIndex >= weeks) continue

    const touchedTypes = new Set<string>()
    for (const id of commit.objectIds) {
      const type = typeById.get(id)
      if (type) touchedTypes.add(type)
    }
    for (const type of touchedTypes) {
      const row = matrix.get(type)
      if (row) row[weekIndex] += 1
    }
  }

  return (
    <ul className="type-heatmap-list">
      {types.map((type) => {
        const row = matrix.get(type) ?? []
        const total = row.reduce((a, b) => a + b, 0)
        const color = colorForType(type)
        const rowMax = Math.max(1, ...row)
        return (
          <li key={type} className="type-heatmap-row">
            <TypeBadge type={type} />
            <span className="type-heatmap-count">{total}</span>
            <span className="type-heatmap-cells">
              {row.map((count, i) => (
                <span
                  key={weekStarts[i].toISOString()}
                  className="type-heatmap-cell"
                  title={`Week of ${weekStarts[i].toLocaleDateString()}: ${count} ${type} change${count === 1 ? '' : 's'}`}
                  style={count > 0 ? cellStyle(color, intensity(count, rowMax)) : undefined}
                />
              ))}
            </span>
          </li>
        )
      })}
    </ul>
  )
}

function cellStyle(color: string, level: number): CSSProperties {
  return {
    background: `color-mix(in srgb, ${color} ${Math.round(level * 100)}%, var(--surface-alt))`,
    borderColor: color,
  }
}
