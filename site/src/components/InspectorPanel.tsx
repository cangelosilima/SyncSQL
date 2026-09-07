import { useId, type ReactNode } from 'react'

/** Persistent Lineage inspector; stacks beneath the graph on narrow screens. */
export default function InspectorPanel({ title = 'Inspector', children }: {
  title?: string; children: ReactNode
}) {
  const id = useId()
  return <div className="inspector-host inspector-host--open">
    <aside className="inspector-panel" aria-labelledby={id}>
      <div className="inspector-heading"><h2 id={id}>{title}</h2></div>
      {children}
    </aside>
  </div>
}
