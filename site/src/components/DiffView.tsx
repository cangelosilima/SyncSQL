import { useMemo } from 'react'
import { diffLines } from '../lib/diff'

interface DiffViewProps {
  oldText: string
  newText: string
  oldLabel: string
  newLabel: string
}

/**
 * Side-by-side line diff between two DDL revisions. Plain monospace text
 * (no syntax highlighting) like most diff tools - color-coded backgrounds
 * carry the signal instead.
 */
export default function DiffView({ oldText, newText, oldLabel, newLabel }: DiffViewProps) {
  const lines = useMemo(() => diffLines(oldText, newText), [oldText, newText])

  if (oldText === newText) {
    return <p className="muted">No differences between these two revisions.</p>
  }

  if (lines === null) {
    return <p className="muted">One of these revisions is too large to diff in the browser.</p>
  }

  return (
    <div className="diff-view">
      <div className="diff-view-header">
        <span className="diff-view-col-label diff-view-col-label--old">{oldLabel}</span>
        <span className="diff-view-col-label diff-view-col-label--new">{newLabel}</span>
      </div>
      <div className="diff-view-body">
        {lines.map((line, i) => (
          <div key={i} className="diff-row">
            <span
              className={`diff-cell diff-cell--old${line.type === 'delete' ? ' diff-cell--del' : line.type === 'insert' ? ' diff-cell--blank' : ''}`}
            >
              <span className="diff-line-no">{line.oldLineNo ?? ''}</span>
              <span className="diff-line-text">{line.type !== 'insert' ? line.text : ''}</span>
            </span>
            <span
              className={`diff-cell diff-cell--new${line.type === 'insert' ? ' diff-cell--ins' : line.type === 'delete' ? ' diff-cell--blank' : ''}`}
            >
              <span className="diff-line-no">{line.newLineNo ?? ''}</span>
              <span className="diff-line-text">{line.type !== 'delete' ? line.text : ''}</span>
            </span>
          </div>
        ))}
      </div>
    </div>
  )
}
