import { colorForType } from '../lib/typeColors'

export default function TypeBadge({ type }: { type: string }) {
  const color = colorForType(type)
  return (
    <span className="type-badge" style={{ '--badge-color': color } as React.CSSProperties}>
      {type}
    </span>
  )
}
