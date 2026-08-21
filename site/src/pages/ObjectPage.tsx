import { useEffect, useMemo, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { useCatalog } from '../lib/CatalogContext'
import CodeBlock from '../components/CodeBlock'
import TypeBadge from '../components/TypeBadge'
import LineageGraph from '../components/LineageGraph'
import { getEdgeColumns } from '../lib/neighborhood'
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
                <th>Type</th>
                <th>Description</th>
              </tr>
            </thead>
            <tbody>
              {node.columns.map((col) => (
                <tr key={col.name}>
                  <td>{col.name}</td>
                  <td className="mono-cell">{col.dataType ?? <span className="muted">-</span>}</td>
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

      {node.grants.length > 0 && (
        <>
          <div className="lineage-graph-header">
            <h2>Access</h2>
            <Link to="/lineage?tab=access">Search access by grantee &rarr;</Link>
          </div>
          <table className="columns-table">
            <thead>
              <tr>
                <th>Grantee</th>
                <th>Type</th>
                <th>Permission</th>
                <th>State</th>
                <th>Column</th>
              </tr>
            </thead>
            <tbody>
              {node.grants.map((grant, i) => (
                <tr key={`${grant.grantee}-${grant.permission}-${grant.column ?? ''}-${i}`}>
                  <td>
                    <Link to={`/lineage?tab=access&grantee=${encodeURIComponent(grant.grantee)}`}>{grant.grantee}</Link>
                  </td>
                  <td>{grant.granteeType ?? <span className="muted">-</span>}</td>
                  <td>{grant.permission}</td>
                  <td>
                    <span className={grant.state === 'DENY' ? 'grant-state grant-state--deny' : 'grant-state grant-state--grant'}>
                      {grant.state}
                    </span>
                  </td>
                  <td>{grant.column ?? <span className="muted">(whole object)</span>}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </>
      )}

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
      <p className="muted overview-panel-hint">
        Column tags are a best-effort signal (qualified &quot;alias.column&quot; references detected in the DDL text), not a
        certified column-level lineage report.
      </p>
      <div className="lineage-lists">
        <div>
          <h3>Depends on ({outgoing.length})</h3>
          <RelatedList rootId={node.id} ids={outgoing} direction="outgoing" />
        </div>
        <div>
          <h3>Used by ({incoming.length})</h3>
          <RelatedList rootId={node.id} ids={incoming} direction="incoming" />
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

function RelatedList({ rootId, ids, direction }: { rootId: string; ids: string[]; direction: 'outgoing' | 'incoming' }) {
  const { index } = useCatalog()
  if (ids.length === 0) return <p className="muted">None found.</p>
  return (
    <ul className="related-list">
      {ids.map((id) => {
        const target = index?.byId.get(id)
        if (!target || !index) return null
        const columns = direction === 'outgoing' ? getEdgeColumns(index, rootId, id) : getEdgeColumns(index, id, rootId)
        return (
          <li key={id}>
            <Link to={`/object/${id}`}>{target.qualifiedName}</Link> <TypeBadge type={target.type} />
            {columns.length > 0 && <ColumnTags columns={columns} />}
          </li>
        )
      })}
    </ul>
  )
}

function ColumnTags({ columns, cap = 6 }: { columns: string[]; cap?: number }) {
  const [expanded, setExpanded] = useState(false)
  const shown = expanded ? columns : columns.slice(0, cap)
  const overflow = columns.length - shown.length
  return (
    <span className="column-tags">
      {shown.map((col) => (
        <span key={col} className="column-tag">
          {col}
        </span>
      ))}
      {overflow > 0 && (
        <button type="button" className="column-tag column-tag--more" onClick={() => setExpanded(true)}>
          +{overflow} more
        </button>
      )}
      {expanded && columns.length > cap && (
        <button type="button" className="column-tag column-tag--more" onClick={() => setExpanded(false)}>
          show less
        </button>
      )}
    </span>
  )
}
