import { useEffect, useMemo, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { useCatalog } from '../lib/CatalogContext'
import CodeBlock from '../components/CodeBlock'
import TypeBadge from '../components/TypeBadge'
import LineageGraph from '../components/LineageGraph'
import type { CatalogObjectVersion } from '../types'

export default function ObjectPage() {
  const params = useParams()
  const id = params['*'] ?? ''
  const { index } = useCatalog()

  const node = index?.byId.get(id)
  const outgoing = index?.outgoing.get(id) ?? []
  const incoming = index?.incoming.get(id) ?? []

  const [viewingVersion, setViewingVersion] = useState<CatalogObjectVersion | null>(null)
  useEffect(() => setViewingVersion(null), [id])

  const neighborhoodIds = useMemo(() => {
    if (!node) return []
    return Array.from(new Set([node.id, ...outgoing, ...incoming]))
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [node?.id, outgoing.join(','), incoming.join(',')])

  if (!index) return null

  if (!node) {
    return (
      <div className="page">
        <h1>Not found</h1>
        <p>No object with id &quot;{id}&quot; in the catalog.</p>
        <Link to="/">Back to overview</Link>
      </div>
    )
  }

  return (
    <div className="page">
      <p className="breadcrumb">
        {node.server} / {node.database}
        {node.schema ? ` / ${node.schema}` : ''}
      </p>
      <h1>
        {node.qualifiedName} <TypeBadge type={node.type} />
      </h1>
      {node.description && <p className="object-description">{node.description}</p>}

      {node.columns.length > 0 && (
        <>
          <h2>Columns</h2>
          <table className="columns-table">
            <thead>
              <tr>
                <th>Name</th>
                <th>Description</th>
              </tr>
            </thead>
            <tbody>
              {node.columns.map((col) => (
                <tr key={col.name}>
                  <td>{col.name}</td>
                  <td>{col.description ?? <span className="muted">-</span>}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </>
      )}

      <h2>Definition</h2>
      {viewingVersion && (
        <div className="version-banner">
          Viewing revision from {new Date(viewingVersion.date).toLocaleString()} ({viewingVersion.sha.slice(0, 7)}):{' '}
          {viewingVersion.message}
          <button type="button" className="version-banner-back" onClick={() => setViewingVersion(null)}>
            Back to latest
          </button>
        </div>
      )}
      <CodeBlock code={viewingVersion ? (viewingVersion.ddl ?? '-- Not available at this revision.') : node.ddl} />

      {!viewingVersion &&
        node.sections.map((section) => (
          <details key={section.title} className="object-section">
            <summary>{section.title}</summary>
            <CodeBlock code={section.content} />
          </details>
        ))}

      {node.history.length > 0 && (
        <>
          <h2>Change history</h2>
          <p className="muted overview-panel-hint">
            {node.changeCount} change{node.changeCount === 1 ? '' : 's'} in the mined commit window. Click a revision to
            view its definition as of that commit.
          </p>
          <ul className="history-list">
            {node.history.map((version) => (
              <li key={version.sha}>
                <button
                  type="button"
                  className={viewingVersion?.sha === version.sha ? 'history-entry active' : 'history-entry'}
                  onClick={() => setViewingVersion(version)}
                  disabled={!version.ddl}
                  title={version.ddl ? 'View this revision' : 'Content not available for this revision'}
                >
                  <span className="history-date">{new Date(version.date).toLocaleDateString()}</span>
                  <span className="history-message">{version.message}</span>
                  <span className="history-sha">{version.sha.slice(0, 7)}</span>
                </button>
              </li>
            ))}
          </ul>
        </>
      )}

      <h2>Lineage</h2>
      <div className="lineage-lists">
        <div>
          <h3>Depends on ({outgoing.length})</h3>
          <RelatedList ids={outgoing} />
        </div>
        <div>
          <h3>Used by ({incoming.length})</h3>
          <RelatedList ids={incoming} />
        </div>
      </div>

      {neighborhoodIds.length > 1 && (
        <>
          <div className="lineage-graph-header">
            <h3>Neighborhood graph</h3>
            <Link to={`/lineage?focus=${encodeURIComponent(node.id)}`}>Open in full lineage explorer &rarr;</Link>
          </div>
          <LineageGraph nodeIds={neighborhoodIds} focusId={node.id} height={360} />
        </>
      )}
    </div>
  )
}

function RelatedList({ ids }: { ids: string[] }) {
  const { index } = useCatalog()
  if (ids.length === 0) return <p className="muted">None found.</p>
  return (
    <ul className="related-list">
      {ids.map((id) => {
        const target = index?.byId.get(id)
        if (!target) return null
        return (
          <li key={id}>
            <Link to={`/object/${id}`}>{target.qualifiedName}</Link> <TypeBadge type={target.type} />
          </li>
        )
      })}
    </ul>
  )
}
