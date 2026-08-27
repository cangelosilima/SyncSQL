import { useEffect, useMemo, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { useCatalog } from '../lib/CatalogContext'
import CodeBlock from '../components/CodeBlock'
import TypeBadge from '../components/TypeBadge'
import LineageGraph from '../components/LineageGraph'
import MetricsPanels from '../components/MetricsPanels'
import DiffView from '../components/DiffView'
import { getEdgeColumns } from '../lib/neighborhood'
import type { CatalogNode, CatalogObjectVersion } from '../types'

/** Synthetic sha standing in for the object's current (uncommitted-to-history) DDL, selectable in compare mode alongside real revisions. */
const CURRENT_SHA = '__current__'

export default function ObjectPage() {
  const params = useParams()
  const id = params['*'] ?? ''
  const { index } = useCatalog()

  const node = index?.byId.get(id)
  const outgoing = index?.outgoing.get(id) ?? []
  const incoming = index?.incoming.get(id) ?? []
  const orphanedRefs = index?.orphanedByFrom.get(id) ?? []

  const [viewingVersion, setViewingVersion] = useState<CatalogObjectVersion | null>(null)
  const [compareMode, setCompareMode] = useState(false)
  const [diffPicks, setDiffPicks] = useState<string[]>([])
  useEffect(() => {
    setViewingVersion(null)
    setCompareMode(false)
    setDiffPicks([])
  }, [id])

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
        <Link to="/explorer">Explorer</Link> / {node.qualifiedName}
      </p>
      <h1>
        {node.qualifiedName} <TypeBadge type={node.type} />
      </h1>
      <p className="breadcrumb">
        {node.server} &rarr; {node.database}
        {node.schema ? ` → ${node.schema}` : ''}
      </p>
      {node.description && <p className="object-description">{node.description}</p>}

      <div className="object-quick-facts">
        {node.lastChangedAt && (
          <div>
            <span className="quick-stat-label">Modified</span>
            <span>{new Date(node.lastChangedAt).toLocaleDateString()}</span>
          </div>
        )}
        <div>
          <span className="quick-stat-label">Deps</span>
          <span>{outgoing.length}</span>
        </div>
        <div>
          <span className="quick-stat-label">Used by</span>
          <span>{incoming.length}</span>
        </div>
        {node.columns.length > 0 && (
          <div>
            <span className="quick-stat-label">Columns</span>
            <span>{node.columns.length}</span>
          </div>
        )}
        <Link className="lineage-share-btn" to={`/lineage?focus=${encodeURIComponent(node.id)}`}>
          Open in Lineage &rarr;
        </Link>
      </div>

      {orphanedRefs.length > 0 && (
        <div className="orphaned-ref-warning">
          <strong>
            {orphanedRefs.length} orphaned reference{orphanedRefs.length === 1 ? '' : 's'}
          </strong>{' '}
          - this object&apos;s DDL refers to something that doesn&apos;t resolve in the current catalog&apos;s scope,
          usually a renamed or dropped target:
          <ul className="orphaned-ref-list">
            {orphanedRefs.map((ref, i) => (
              <li key={`${ref.schema ?? ''}|${ref.name}|${i}`}>{ref.schema ? `${ref.schema}.${ref.name}` : ref.name}</li>
            ))}
          </ul>
        </div>
      )}

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

      {node.metrics.length > 0 && (
        <>
          <h2>Metrics</h2>
          <p className="muted overview-panel-hint">
            Volume, index and optimizer-statistics history mined at extraction time - kept separate from this
            object&apos;s own version history since it changes on every run.
          </p>
          <MetricsPanels metrics={node.metrics} />
        </>
      )}

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
          <div className="lineage-graph-header">
            <h2>Change history</h2>
            <button
              type="button"
              className="lineage-share-btn"
              onClick={() => {
                setCompareMode((v) => !v)
                setDiffPicks([])
                setViewingVersion(null)
              }}
            >
              {compareMode ? 'Cancel comparison' : 'Compare two revisions'}
            </button>
          </div>
          <p className="muted overview-panel-hint">
            {compareMode
              ? 'Pick two revisions (including "Current") to see a side-by-side diff.'
              : `${node.changeCount} change${node.changeCount === 1 ? '' : 's'} in the mined commit window. Click a revision to view its definition as of that commit.`}
          </p>
          <ul className="history-list">
            {[{ sha: CURRENT_SHA, date: new Date().toISOString(), message: 'Current definition', ddl: node.ddl }, ...node.history].map(
              (version) => {
                const available = Boolean(version.ddl)
                const picked = diffPicks.includes(version.sha)
                return (
                  <li key={version.sha}>
                    <button
                      type="button"
                      className={
                        (compareMode ? picked : viewingVersion?.sha === version.sha) ? 'history-entry active' : 'history-entry'
                      }
                      onClick={() => {
                        if (compareMode) {
                          if (!available) return
                          setDiffPicks((prev) => {
                            if (prev.includes(version.sha)) return prev.filter((s) => s !== version.sha)
                            if (prev.length >= 2) return [prev[1], version.sha]
                            return [...prev, version.sha]
                          })
                        } else {
                          setViewingVersion(version.sha === CURRENT_SHA ? null : version)
                        }
                      }}
                      disabled={!available}
                      title={available ? (compareMode ? 'Select for comparison' : 'View this revision') : 'Content not available for this revision'}
                    >
                      {compareMode && <span className="history-entry-check">{picked ? '✓' : ''}</span>}
                      <span className="history-date">
                        {version.sha === CURRENT_SHA ? '' : new Date(version.date).toLocaleDateString()}
                      </span>
                      <span className="history-message">{version.message}</span>
                      <span className="history-sha">{version.sha === CURRENT_SHA ? '' : version.sha.slice(0, 7)}</span>
                    </button>
                  </li>
                )
              },
            )}
          </ul>

          {compareMode && diffPicks.length === 2 && (
            <DiffCompare node={node} shas={diffPicks} />
          )}
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

function resolveDiffPick(node: CatalogNode, sha: string): { date: string; label: string; ddl: string | null } {
  if (sha === CURRENT_SHA) {
    return { date: new Date().toISOString(), label: 'Current definition', ddl: node.ddl }
  }
  const version = node.history.find((v) => v.sha === sha)
  return {
    date: version?.date ?? '',
    label: version ? `${new Date(version.date).toLocaleDateString()} (${version.sha.slice(0, 7)}) - ${version.message}` : sha,
    ddl: version?.ddl ?? null,
  }
}

/** Orders the two picked revisions old-to-new regardless of click order, then renders the diff between them. */
function DiffCompare({ node, shas }: { node: CatalogNode; shas: string[] }) {
  const [shaA, shaB] = shas
  const a = resolveDiffPick(node, shaA)
  const b = resolveDiffPick(node, shaB)
  const [older, newer] = a.date <= b.date ? [a, b] : [b, a]

  if (older.ddl === null || newer.ddl === null) {
    return <p className="muted">Content not available for one of the selected revisions.</p>
  }

  return (
    <>
      <h3>Diff</h3>
      <DiffView oldText={older.ddl} newText={newer.ddl} oldLabel={older.label} newLabel={newer.label} />
    </>
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
