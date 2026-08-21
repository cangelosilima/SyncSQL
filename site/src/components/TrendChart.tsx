interface TrendSeries {
  name: string
  color: string
  values: (number | null)[]
}

interface TrendChartProps {
  /** X-axis labels, same length as every series' values array. */
  labels: string[]
  series: TrendSeries[]
  height?: number
  formatValue?: (v: number) => string
}

const WIDTH = 640
const PAD_X = 10
const PAD_TOP = 10
const PAD_BOTTOM = 20

/**
 * Minimal dependency-free multi-series line chart (inline SVG) for trending
 * volatile metrics (row counts, size, fragmentation, ...) over time. Shares
 * one y-scale across all series; null values render as gaps rather than
 * interpolated points. Point-level detail is exposed via native <title>
 * tooltips instead of a JS hover layer, keeping this dependency-free.
 */
export default function TrendChart({ labels, series, height = 160, formatValue = (v) => String(v) }: TrendChartProps) {
  const allValues = series.flatMap((s) => s.values).filter((v): v is number => v !== null && v !== undefined)

  if (allValues.length === 0) {
    return <div className="trend-chart-empty">No data points yet.</div>
  }

  const min = Math.min(0, ...allValues)
  const max = Math.max(...allValues)
  const range = max - min || 1
  const n = labels.length

  function xFor(i: number) {
    return n <= 1 ? WIDTH / 2 : (i / (n - 1)) * (WIDTH - PAD_X * 2) + PAD_X
  }
  function yFor(v: number) {
    return height - PAD_BOTTOM - ((v - min) / range) * (height - PAD_TOP - PAD_BOTTOM)
  }

  const paths = series.map((s) => {
    let d = ''
    let drawing = false
    s.values.forEach((v, i) => {
      if (v === null || v === undefined) {
        drawing = false
        return
      }
      d += `${drawing ? 'L' : 'M'}${xFor(i).toFixed(1)},${yFor(v).toFixed(1)} `
      drawing = true
    })
    return { ...s, d: d.trim() }
  })

  return (
    <div className="trend-chart">
      <svg viewBox={`0 0 ${WIDTH} ${height}`} className="trend-chart-svg" preserveAspectRatio="none" role="img">
        <line x1={PAD_X} y1={yFor(min)} x2={WIDTH - PAD_X} y2={yFor(min)} className="trend-chart-axis" />
        {paths.map(
          (s) =>
            s.d && <path key={s.name} d={s.d} fill="none" stroke={s.color} strokeWidth={1.75} className="trend-chart-line" />,
        )}
        {paths.map((s) =>
          s.values.map((v, i) =>
            v === null || v === undefined ? null : (
              <circle key={`${s.name}-${i}`} cx={xFor(i)} cy={yFor(v)} r={2.4} fill={s.color}>
                <title>{`${labels[i]} - ${s.name}: ${formatValue(v)}`}</title>
              </circle>
            ),
          ),
        )}
      </svg>
      <div className="trend-chart-footer">
        <div className="trend-chart-legend">
          {series.map((s) => (
            <span key={s.name} className="trend-chart-legend-item">
              <span className="trend-chart-swatch" style={{ background: s.color }} />
              {s.name}
            </span>
          ))}
        </div>
        {n > 0 && (
          <span className="trend-chart-range">
            {labels[0]}
            {n > 1 ? ` → ${labels[n - 1]}` : ''}
          </span>
        )}
      </div>
    </div>
  )
}
