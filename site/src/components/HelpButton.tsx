import { useEffect, useId, useRef, useState } from 'react'
import { createPortal } from 'react-dom'
import Markdown from './Markdown'
import { helpGuides, type HelpTopic } from '../help'

interface HelpButtonProps {
  /** Which guide in src/help/ this page is documented by. */
  topic: HelpTopic
  className?: string
}

/**
 * The "?" affordance every page carries next to its heading. Clicking it opens
 * that page's Markdown guide in a dialog rather than navigating away, so the
 * view you were reading - filters, drill-down, scroll position - survives
 * reading the help.
 *
 * The dialog is portaled to <body> because the trigger sits inside a page's
 * <h1>: rendering in place would inherit the heading's typography and its
 * stacking context.
 */
export default function HelpButton({ topic, className = '' }: HelpButtonProps) {
  const [open, setOpen] = useState(false)
  const guide = helpGuides[topic]
  const titleId = useId()
  const triggerRef = useRef<HTMLButtonElement>(null)
  const panelRef = useRef<HTMLDivElement>(null)

  useEffect(() => {
    if (!open) return
    panelRef.current?.focus()
    function onKeyDown(event: KeyboardEvent) {
      if (event.key === 'Escape') {
        event.stopPropagation()
        setOpen(false)
      }
    }
    document.addEventListener('keydown', onKeyDown)
    return () => document.removeEventListener('keydown', onKeyDown)
  }, [open])

  function close() {
    setOpen(false)
    triggerRef.current?.focus()
  }

  return (
    <>
      <button
        type="button"
        ref={triggerRef}
        className={`help-button ${className}`.trim()}
        aria-haspopup="dialog"
        aria-expanded={open}
        title={`Help: ${guide.title}`}
        aria-label={`Help: ${guide.title}`}
        onClick={() => setOpen((v) => !v)}
      >
        ?
      </button>
      {open &&
        createPortal(
          <div className="help-overlay" role="presentation" onClick={close}>
            <div
              ref={panelRef}
              className="help-panel"
              role="dialog"
              aria-modal="true"
              aria-labelledby={titleId}
              tabIndex={-1}
              onClick={(event) => event.stopPropagation()}
            >
              <div className="help-panel-header">
                <div>
                  <span className="help-panel-eyebrow">Page guide</span>
                  <h2 id={titleId}>{guide.title}</h2>
                </div>
                <button type="button" className="help-close" onClick={close} aria-label="Close help">
                  &times;
                </button>
              </div>
              <div className="help-panel-body">
                <Markdown source={guide.body} className="markdown help-markdown" />
              </div>
            </div>
          </div>,
          document.body,
        )}
    </>
  )
}
