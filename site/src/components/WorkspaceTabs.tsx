import { type ReactNode } from 'react'

export default function WorkspaceTabs<T extends string>({ label, items, value, onChange, panelPrefix }: {
  label: string
  items: readonly T[]
  value: T
  onChange: (value: T) => void
  panelPrefix?: string
}) {
  return <div className="workspace-tabs" role="tablist" aria-label={label}>
    {items.map((item, i) => <button key={item} type="button" role="tab"
      id={panelPrefix ? `${panelPrefix}-tab-${item}` : undefined} aria-controls={panelPrefix ? `${panelPrefix}-${item}` : undefined}
      aria-selected={value === item} tabIndex={value === item ? 0 : -1}
      onClick={() => onChange(item)} onKeyDown={(event) => {
        const next = event.key === 'Home' ? 0 : event.key === 'End' ? items.length - 1
          : event.key === 'ArrowRight' ? (i + 1) % items.length
            : event.key === 'ArrowLeft' ? (i + items.length - 1) % items.length : null
        if (next === null) return
        event.preventDefault()
        onChange(items[next])
        const buttons = event.currentTarget.parentElement?.querySelectorAll<HTMLButtonElement>('[role="tab"]')
        buttons?.[next].focus()
      }}>{item}</button>)}
  </div>
}

export function WorkspacePanel({ name, active, children, panelPrefix = 'object-workspace' }: { name: string; active: string; children: ReactNode; panelPrefix?: string }) {
  // Keep local section state mounted while changing workspace; hidden sections
  // are excluded from the accessibility tree and cannot receive keyboard focus.
  return <section id={`${panelPrefix}-${name}`} role="tabpanel" aria-label={name} aria-labelledby={`${panelPrefix}-tab-${name}`} hidden={name !== active} tabIndex={0}>{children}</section>
}
