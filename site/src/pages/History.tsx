import { useState } from 'react'
import { Link } from 'react-router-dom'
import { useCatalog } from '../lib/CatalogContext'
import TypeBadge from '../components/TypeBadge'

export default function History() {
  const { index } = useCatalog()
  const [expanded, setExpanded] = useState<Set<string>>(new Set())

  if (!index) return null
  const commits = index.catalog.recentChanges

  function toggle(sha: string) {
    setExpanded((prev) => {
      const next = new Set(prev)
      if (next.has(sha)) next.delete(sha)
      else next.add(sha)
      return next
    })
  }

  return (
    <div className="page">
      <h1>History</h1>
      <p className="muted">
        {commits.length} commit{commits.length === 1 ? '' : 's'} touching tracked objects, most recent first (mined
        by the analyze-catalog CI stage, bounded to a configurable commit window).
      </p>

      {commits.length === 0 ? (
        <p className="muted">No history was mined for this run.</p>
      ) : (
        <ul className="commit-list">
          {commits.map((commit) => {
            const isOpen = expanded.has(commit.sha)
            return (
              <li key={commit.sha} className="commit-entry">
                <button type="button" className="commit-summary" onClick={() => toggle(commit.sha)}>
                  <span className="commit-toggle">{isOpen ? '▾' : '▸'}</span>
                  <span className="commit-date">{new Date(commit.date).toLocaleString()}</span>
                  <span className="commit-message">{commit.message}</span>
                  <span className="commit-count">
                    {commit.objectIds.length} object{commit.objectIds.length === 1 ? '' : 's'}
                  </span>
                  <span className="commit-sha">{commit.sha.slice(0, 7)}</span>
                </button>
                {isOpen && (
                  <ul className="commit-objects">
                    {commit.objectIds.map((id) => {
                      const node = index.byId.get(id)
                      if (!node) return null
                      return (
                        <li key={id}>
                          <Link to={`/object/${id}`}>{node.qualifiedName}</Link> <TypeBadge type={node.type} />
                        </li>
                      )
                    })}
                  </ul>
                )}
              </li>
            )
          })}
        </ul>
      )}
    </div>
  )
}
