import { useEffect, useMemo, useState } from 'react'
import { Link, useParams, useSearchParams } from 'react-router-dom'
import { useCatalog } from '../lib/CatalogContext'
import CodeBlock from '../components/CodeBlock'
import TypeBadge from '../components/TypeBadge'
import LineageGraph from '../components/LineageGraph'
import MetricsPanels from '../components/MetricsPanels'
import DiffView from '../components/DiffView'
import RelatedObjects from '../components/RelatedObjects'
import CsvExportButton from '../components/CsvExportButton'
import XlsxExportButton from '../components/XlsxExportButton'
import HelpButton from '../components/HelpButton'
import WorkspaceTabs, { WorkspacePanel } from '../components/WorkspaceTabs'
import { isLinkNode, qualifiedRefName } from '../lib/catalog'
import { getDirectionalNeighborhood } from '../lib/neighborhood'
import { epochOf } from '../lib/analytics'
import { csvFileName } from '../lib/csv'
import { xlsxFileName } from '../lib/xlsx'
import { objectWorkbookSheets } from '../lib/catalogXlsx'
import { columnColumns, dependencyColumns, dependencyRows, grantColumns, objectColumns } from '../lib/catalogCsv'
import { getColumnConsumers, getColumnUsageCount, getColumnUsageCounts } from '../lib/columnLineage'
import type { CatalogNode, CatalogObjectVersion } from '../types'

/** Synthetic sha standing in for the object's current (uncommitted-to-history) DDL, selectable in compare mode alongside real revisions. */
const CURRENT_SHA = '__current__'

/**
 * The inline neighborhood graph is a thumbnail, not the lineage explorer, so it
 * summarizes earlier than the full-page graph does - past this many neighbours
 * the same-type ones collapse into counted bundle nodes.
 */
const NEIGHBORHOOD_GRAPH_CAP = 30
const WORKSPACES = ['Columns', 'Graph', 'Access', 'Relationships', 'Metrics', 'History', 'Diff'] as const
type Workspace = typeof WORKSPACES[number]

export default function ObjectPage() {
  const params = useParams()
  const id = params['*'] ?? ''
  const { index } = useCatalog()

  const node = index?.byId.get(id)
  const outgoing = index?.outgoing.get(id) ?? []
  const incoming = index?.incoming.get(id) ?? []
  const orphanedRefs = index?.orphanedByFrom.get(id) ?? []
  const systemRefs = index?.systemRefsByFrom.get(id) ?? []
  const refsThroughThisLink = index?.linkedServerRefsByLink.get(id) ?? []
  const refsAcrossLinks = index?.linkedServerRefsByFrom.get(id) ?? []

  // Everything reached through this link, one row per remote object with the callers that use it -
  // the same reference made by five procedures is one remote object, not five findings.
  const targetsThroughThisLink = useMemo(() => {
    const byTarget = new Map<string, { label: string; to: string | null; callers: string[] }>()
    for (const ref of refsThroughThisLink) {
      const label = qualifiedRefName(ref)
      const key = ref.to ?? `?${label}`
      if (!byTarget.has(key)) byTarget.set(key, { label, to: ref.to, callers: [] })
      byTarget.get(key)!.callers.push(ref.from)
    }
    return [...byTarget.values()].sort((a, b) => a.label.localeCompare(b.label))
  }, [refsThroughThisLink])

  // Which column's lineage is open, mirrored to ?column= so the view is linkable -
  // "here is who reads Orders.CustomerId" is a thing worth sending someone.
  const [searchParams, setSearchParams] = useSearchParams()
  const selectedColumn = searchParams.get('column')
  const [workspace, setWorkspace] = useState<Workspace>('Columns')
  const [formatted, setFormatted] = useState(false)
  useEffect(() => { setWorkspace('Columns') }, [id])
  useEffect(() => { if (selectedColumn) setWorkspace('Columns') }, [selectedColumn])
  function selectColumn(column: string | null) {
    const next = new URLSearchParams(searchParams)
    if (column) next.set('column', column)
    else next.delete('column')
    setSearchParams(next, { replace: true })
  }

  const columnUsage = useMemo(() => (index ? getColumnUsageCounts(index, id) : new Map<string, number>()), [index, id])
  const columnConsumers = useMemo(
    () => (index && selectedColumn ? getColumnConsumers(index, id, selectedColumn) : []),
    [index, id, selectedColumn],
  )

  const [viewingVersion, setViewingVersion] = useState<CatalogObjectVersion | null>(null)
  const [compareMode, setCompareMode] = useState(false)
  const [diffPicks, setDiffPicks] = useState<string[]>([])
  useEffect(() => {
    setViewingVersion(null)
    setCompareMode(false)
    setDiffPicks([])
  }, [id])

  const neighborhoodIds = useMemo(() => {
    if (!index || !index.byId.has(id)) return []
    return getDirectionalNeighborhood(index, id, 1, 1)
  }, [index, id])

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

  const revisionControls = <>
      {node.history.length === 0 && <p className="empty-state">No history was mined for this object.</p>}
      {workspace === 'Diff' && !compareMode && <p className="muted">Enable comparison and select two available revisions below.</p>}
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
                setWorkspace(compareMode ? 'History' : 'Diff')
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
                          setWorkspace('History')
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

        </>
      )}
  </>

  return (
    <div className="page page--wide object-workbench">
      <p className="breadcrumb">
        <Link to="/explorer">Explorer</Link> / {node.qualifiedName}
      </p>
      <h1 className="page-title">
        Object explorer
        <HelpButton topic="object" />
      </h1>
      <p className="breadcrumb">
        {node.server} &rarr; {node.database}
        {node.schema ? ` → ${node.schema}` : ''}
      </p>
      {node.description && <p className="object-description">{node.description}</p>}

      <div className="object-quick-facts">
        <TypeBadge type={node.type} />
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
        <CsvExportButton
          rows={[node]}
          columns={objectColumns(index)}
          filename={csvFileName(node.id, 'details')}
          label="Export details CSV"
        />
        <XlsxExportButton
          sheets={() => objectWorkbookSheets(index, node)}
          filename={xlsxFileName(node.id)}
          title="Download every section of this page as one workbook - a worksheet per section"
        />
      </div>

      {orphanedRefs.length > 0 && (
        <div className="orphaned-ref-warning">
          <strong>
            {orphanedRefs.length} orphaned reference{orphanedRefs.length === 1 ? '' : 's'}
          </strong>{' '}
          - this object&apos;s DDL refers to something that doesn&apos;t resolve anywhere the lookup reaches (this
          database, the rest of this server, or a server one linked server away), usually a renamed or dropped
          target:
          <ul className="orphaned-ref-list">
            {orphanedRefs.map((ref, i) => (
              <li key={`${ref.server ?? ''}|${ref.database ?? ''}|${ref.schema ?? ''}|${ref.name}|${i}`}>
                {qualifiedRefName(ref)}
              </li>
            ))}
          </ul>
        </div>
      )}

      <section className="object-definition" aria-labelledby="object-definition-title">
      <div className="object-definition-header">
        <h2 id="object-definition-title">Definition</h2>
        <div className="definition-view-toggle" role="group" aria-label="Definition view">
          <button type="button" aria-pressed={!formatted} onClick={() => setFormatted(false)}>Original</button>
          <button type="button" aria-pressed={formatted} onClick={() => setFormatted(true)}>Formatted</button>
        </div>
      </div>
      {viewingVersion && (
        <div className="version-banner">
          Viewing revision from {new Date(viewingVersion.date).toLocaleString()} ({viewingVersion.sha.slice(0, 7)}):{' '}
          {viewingVersion.message}
          <button type="button" className="version-banner-back" onClick={() => setViewingVersion(null)}>
            Back to latest
          </button>
        </div>
      )}
      <CodeBlock code={viewingVersion ? (viewingVersion.ddl ?? '-- Not available at this revision.') : node.ddl} formatted={formatted} />

      {!viewingVersion &&
        node.sections.map((section) => (
          <details key={section.title} className="object-section">
            <summary>{section.title}</summary>
            <CodeBlock code={section.content} formatted={formatted} />
          </details>
        ))}

      </section>
      <WorkspaceTabs panelPrefix="object-workspace" label="Object workspaces" items={WORKSPACES} value={workspace} onChange={setWorkspace} />
      {viewingVersion && <p className="version-banner" role="status">Historical definition selected. Object metadata, grants and metrics describe the current catalog snapshot.</p>}
      <div className="investigation-layout">
      <div className="workspace-content">
      <WorkspacePanel name="Columns" active={workspace}>
      {node.columns.length === 0 && <p className="empty-state">No columns are recorded for this object.</p>}
      {node.columns.length > 0 && (
        <>
          <div className="lineage-graph-header">
            <h2>Columns</h2>
            <CsvExportButton rows={node.columns} columns={columnColumns} filename={csvFileName(node.id, 'columns')} />
          </div>
          <table className="columns-table">
            <thead>
              <tr>
                <th>Name</th>
                <th>Type</th>
                <th>Description</th>
                <th>Used by</th>
              </tr>
            </thead>
            <tbody>
              {node.columns.map((col) => {
                const uses = getColumnUsageCount(columnUsage, col.name)
                const open = selectedColumn?.toLowerCase() === col.name.toLowerCase()
                return (
                  <tr key={col.name} className={open ? 'columns-row--selected' : undefined}>
                    <td>{col.name}</td>
                    <td className="mono-cell">{col.dataType ?? <span className="muted">-</span>}</td>
                    <td>{col.description ?? <span className="muted">-</span>}</td>
                    <td>
                      <button
                        type="button"
                        className="column-lineage-btn"
                        aria-expanded={open}
                        onClick={() => selectColumn(open ? null : col.name)}
                        title={
                          uses > 0
                            ? `Show the ${uses} object(s) known to reference ${col.name}`
                            : `No object in the catalog is known to reference ${col.name}`
                        }
                      >
                        {uses > 0 ? `${uses} object${uses === 1 ? '' : 's'}` : 'none'}
                        <span className="column-lineage-btn-caret" aria-hidden="true">
                          {open ? ' ▾' : ' ▸'}
                        </span>
                      </button>
                    </td>
                  </tr>
                )
              })}
            </tbody>
          </table>

          {selectedColumn && (
            <ColumnLineagePanel
              node={node}
              column={selectedColumn}
              consumers={columnConsumers}
              onClose={() => selectColumn(null)}
            />
          )}
        </>
      )}

      </WorkspacePanel>
      <WorkspacePanel name="Metrics" active={workspace}>
      {node.metrics.length === 0 && <p className="empty-state">No metric snapshots were collected for this object.</p>}
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

      </WorkspacePanel>
      <WorkspacePanel name="Access" active={workspace}>
      {node.grants.length === 0 && <p className="empty-state">No grants are recorded for this object. This does not establish that nobody can access it.</p>}
      {node.grants.length > 0 && (
        <>
          <div className="lineage-graph-header">
            <h2>Access</h2>
            <div className="section-actions">
              <Link to="/lineage?tab=access">Search access by grantee &rarr;</Link>
              <CsvExportButton rows={node.grants} columns={grantColumns} filename={csvFileName(node.id, 'grants')} />
            </div>
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

      </WorkspacePanel>
      <WorkspacePanel name="History" active={workspace}>{revisionControls}</WorkspacePanel>
      <WorkspacePanel name="Diff" active={workspace}>
        {revisionControls}
        {compareMode && diffPicks.length === 2 && <DiffCompare node={node} shas={diffPicks} />}
      </WorkspacePanel>
      <WorkspacePanel name="Graph" active={workspace}>

      {systemRefs.length > 0 && (
        <>
          <h2>System objects referenced</h2>
          <p className="muted overview-panel-hint">
            Objects the database engine provides rather than anything the pipeline extracts - <code>sp_executesql</code>,{' '}
            <code>sys.*</code>, Oracle&apos;s <code>DBMS_*</code>. They resolve to nothing in the catalog, but they are
            not missing, so they are listed here instead of counted as orphaned references.
          </p>
          <span className="column-tags">
            {systemRefs.map((ref, i) => (
              <span key={`${ref.database ?? ''}|${ref.schema ?? ''}|${ref.name}|${i}`} className="column-tag">
                {qualifiedRefName(ref)}
              </span>
            ))}
          </span>
        </>
      )}

      {isLinkNode(node) && (
        <>
          <h2>Referenced through this {node.type === 'DatabaseLinks' ? 'database link' : 'linked server'}</h2>
          {targetsThroughThisLink.length === 0 ? (
            <p className="muted">
              No object in the catalog references anything through this link. Either nothing uses it, or the objects
              that do aren&apos;t extracted.
            </p>
          ) : (
            <table className="columns-table">
              <thead>
                <tr>
                  <th>Referenced by</th>
                  <th>Remote object</th>
                  <th>In catalog</th>
                </tr>
              </thead>
              <tbody>
                {targetsThroughThisLink.map((target) => (
                  <tr key={target.to ?? target.label}>
                    <td>
                      {target.callers.map((caller, i) => (
                        <span key={caller}>
                          {i > 0 && ', '}
                          <Link to={`/object/${caller}`}>{index.byId.get(caller)?.qualifiedName ?? caller}</Link>
                        </span>
                      ))}
                    </td>
                    <td>
                      {target.to ? <Link to={`/object/${target.to}`}>{target.label}</Link> : <code>{target.label}</code>}
                    </td>
                    <td>{target.to ? 'yes' : 'not extracted'}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </>
      )}

      {refsAcrossLinks.length > 0 && (
        <>
          <h2>References across linked servers</h2>
          <table className="columns-table">
            <thead>
              <tr>
                <th>Through</th>
                <th>Remote object</th>
              </tr>
            </thead>
            <tbody>
              {refsAcrossLinks.map((ref, i) => (
                <tr key={`${ref.linkedServer}|${ref.name}|${i}`}>
                  <td>
                    <Link to={`/object/${ref.linkedServer}`}>
                      {index.byId.get(ref.linkedServer)?.name ?? ref.linkedServer}
                    </Link>
                  </td>
                  <td>
                    {ref.to ? (
                      <Link to={`/object/${ref.to}`}>{qualifiedRefName(ref)}</Link>
                    ) : (
                      <>
                        <code>{qualifiedRefName(ref)}</code> <span className="muted">(not extracted)</span>
                      </>
                    )}
                    {ref.dynamic && (
                      <span
                        className="column-tag column-tag--dynamic"
                        title="Recovered from SQL built as a string at runtime (an OPENQUERY body, an EXEC ... AT link) rather than read off the parse tree."
                      >
                        dynamic
                      </span>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </>
      )}

      <div className="lineage-graph-header">
        <h2>Lineage</h2>
        <CsvExportButton
          rows={dependencyRows(index, node.id)}
          columns={dependencyColumns}
          filename={csvFileName(node.id, 'dependencies')}
          label="Export lineage CSV"
          title="Download both directions (depends on + used by) as one CSV"
        />
      </div>
      <p className="muted overview-panel-hint">
        Column tags are a best-effort signal (qualified &quot;alias.column&quot; references detected in the DDL text), not a
        certified column-level lineage report.
      </p>
      <div className="lineage-lists">
        <RelatedObjects title="Used by" rootId={node.id} ids={incoming} direction="incoming" />
        <RelatedObjects title="Depends on" rootId={node.id} ids={outgoing} direction="outgoing" />
      </div>

      {neighborhoodIds.length === 1 && <p className="empty-state">No inferred dependencies or consumers are recorded for this object.</p>}
      {workspace === 'Graph' && neighborhoodIds.length > 1 && (
        <>
          <div className="lineage-graph-header">
            <h3>Neighborhood graph</h3>
            <Link to={`/lineage?focus=${encodeURIComponent(node.id)}`}>Open in full lineage explorer &rarr;</Link>
          </div>
          <LineageGraph nodeIds={neighborhoodIds} focusId={node.id} height={360} maxNodes={NEIGHBORHOOD_GRAPH_CAP} />
        </>
      )}
      </WorkspacePanel>
        <WorkspacePanel name="Relationships" active={workspace}>
          <div className="object-relationships-heading">
            <h2>Relationships</h2>
            <button type="button" className="lineage-share-btn" onClick={() => setWorkspace('Graph')}>All relationship evidence</button>
          </div>
          <RelatedObjects title="Used by" rootId={node.id} ids={incoming} direction="incoming" />
          <RelatedObjects title="Depends on" rootId={node.id} ids={outgoing} direction="outgoing" />
        </WorkspacePanel>
      </div>
      </div>

    </div>
  )
}

/**
 * Lineage for one column: the objects whose DDL was seen reading it, and a graph of just
 * those. Deliberately one-directional - see lib/columnLineage.ts for why "what feeds this
 * column" isn't shown rather than guessed at.
 */
function ColumnLineagePanel({
  node,
  column,
  consumers,
  onClose,
}: {
  node: CatalogNode
  column: string
  consumers: CatalogNode[]
  onClose: () => void
}) {
  return (
    <div className="column-lineage-panel">
      <div className="lineage-graph-header">
        <h3>
          Lineage for <code>{column}</code>
        </h3>
        <div className="section-actions">
          <Link to={`/lineage?focus=${encodeURIComponent(node.id)}`}>Open in Lineage &rarr;</Link>
          <button type="button" className="lineage-nav-clear" onClick={onClose}>
            Close
          </button>
        </div>
      </div>

      {consumers.length === 0 ? (
        <p className="muted">
          No object in the catalog is known to reference <code>{column}</code>. That is not proof nothing does: the
          signal comes from qualified &quot;alias.column&quot; references detected in DDL text, so it misses{' '}
          <code>SELECT *</code>, computed expressions and anything built at runtime.
        </p>
      ) : (
        <>
          <p className="muted overview-panel-hint">
            {consumers.length} object{consumers.length === 1 ? '' : 's'} read{consumers.length === 1 ? 's' : ''}{' '}
            <code>{column}</code>. What <em>feeds</em> this column isn&apos;t shown: that needs expression-level lineage
            the catalog doesn&apos;t record, and a same-name guess would not be an answer.
          </p>
          <ul className="related-list">
            {consumers.map((consumer) => (
              <li key={consumer.id}>
                <Link to={`/object/${consumer.id}`}>{consumer.qualifiedName}</Link> <TypeBadge type={consumer.type} />
              </li>
            ))}
          </ul>
          <LineageGraph nodeIds={[node.id, ...consumers.map((c) => c.id)]} focusId={node.id} height={360} />
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
  const [older, newer] = epochOf(a.date) <= epochOf(b.date) ? [a, b] : [b, a]

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
