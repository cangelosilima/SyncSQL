import { useEffect, useMemo, useState } from 'react'
import { Link, useSearchParams } from 'react-router-dom'
import { useCatalog } from '../lib/CatalogContext'
import LineageGraph, { type EdgeColumnData } from '../components/LineageGraph'
import FilterBar, { useFilteredNodes } from '../components/FilterBar'
import TypeBadge from '../components/TypeBadge'
import CsvExportButton from '../components/CsvExportButton'
import HelpButton from '../components/HelpButton'
import InspectorPanel from '../components/InspectorPanel'
import RelatedObjects from '../components/RelatedObjects'
import { colorForType } from '../lib/typeColors'
import { getDirectionalNeighborhood, retainConnectingPaths } from '../lib/neighborhood'
import { groupRelated, type GroupBy } from '../lib/grouping'
import { csvFileName } from '../lib/csv'
import { objectGrantColumns, type ObjectGrantRow } from '../lib/catalogCsv'
import { decodeTokensFromUrl, encodeTokensForUrl, matchingGrants, newTokenId, type FilterToken } from '../lib/filters'
import type { CatalogNode } from '../types'
import { GRAPH_LEGEND_ITEMS, GRAPH_LEGEND_NOTE } from '../lib/graphLegend'

const GRAPH_CAP = 300
const MAX_HOPS = 20
const HOP_OPTIONS = Array.from({ length: MAX_HOPS + 1 }, (_, hop) => hop)
function parseHops(value: string | null, fallback = 1): number {
  const number = value === null ? fallback : Number(value)
  return Number.isInteger(number) && number >= 0 && number <= MAX_HOPS ? number : fallback
}

export default function LineagePage() {
  const { index } = useCatalog()
  const [searchParams, setSearchParams] = useSearchParams()
  const initialFocus = searchParams.get('focus') ?? undefined
  const initialFilterParam = searchParams.get('filter')
  const initialHops = parseHops(searchParams.get('hops'))
  const [copied, setCopied] = useState(false)
  const [edgeEvidence, setEdgeEvidence] = useState<EdgeColumnData | null>(null)

  // Migrate old access and DDL URLs into the same chip model as object filters.
  const [tokens, setTokens] = useState<FilterToken[]>(() => {
    const initial = decodeTokensFromUrl(initialFilterParam)
    const grantee = searchParams.get('grantee')?.trim()
    const content = searchParams.get('q')?.trim()
    if (grantee)
      initial.push({
        id: newTokenId(),
        attribute: 'grantee',
        operator: searchParams.get('exact') === '1' ? 'is' : 'contains',
        values: [grantee],
      })
    if (content) initial.push({ id: newTokenId(), attribute: 'ddl', operator: 'contains', values: [content] })
    return initial
  })
  const [focusStack, setFocusStack] = useState<string[]>(initialFocus ? [initialFocus] : [])
  const [dependencyHops, setDependencyHops] = useState(() => parseHops(searchParams.get('dependencies'), initialHops))
  const [dependentHops, setDependentHops] = useState(() => parseHops(searchParams.get('dependents'), initialHops))
  const [groupIntermediate, setGroupIntermediate] = useState(searchParams.get('group') === 'intermediate')
  const allNodes = index?.catalog.nodes ?? []
  const filtered = useFilteredNodes(allNodes, tokens)
  const hasGranteeFilter = tokens.some((token) => token.attribute === 'grantee')
  const currentFocus = focusStack[focusStack.length - 1]
  useEffect(() => {
    setEdgeEvidence(null)
  }, [currentFocus])

  const neighborhoodIds = useMemo(() => {
    if (!index || !currentFocus) return []
    return getDirectionalNeighborhood(index, currentFocus, dependencyHops, dependentHops)
  }, [index, currentFocus, dependencyHops, dependentHops])

  const baseIds = filtered.map((node) => node.id)

  // While navigating a specific object, filters narrow *what is around it*
  // rather than re-selecting from the whole catalog. That is what someone
  // adding "Type is StoredProcedures" to a table's neighborhood is asking for
  // - show me the procedures near this - and the previous behaviour (drop the
  // focus, then AND the tokens against every node) answered a different
  // question with an empty graph. The focus object itself is always kept, so
  // the graph never renders rootless no matter how narrow the filter is.
  const nodeIds = useMemo(() => {
    if (!currentFocus) return baseIds
    const allowed = new Set(filtered.map((n) => n.id))
    return index ? retainConnectingPaths(index, currentFocus, neighborhoodIds, allowed) : []
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [index, currentFocus, neighborhoodIds, filtered, baseIds.join(',')])

  const connectorIds = useMemo(() => {
    const matchingIds = new Set(filtered.map((node) => node.id))
    return currentFocus ? nodeIds.filter((id) => id !== currentFocus && !matchingIds.has(id)) : []
  }, [currentFocus, nodeIds, filtered])

  /** How much of the focused object's neighborhood the current filters are hiding. */
  const narrowedFrom = currentFocus ? neighborhoodIds.length : 0

  // One URL captures every filter and navigation setting.
  useEffect(() => {
    const params: Record<string, string> = {}
    if (tokens.length > 0) params.filter = encodeTokensForUrl(tokens)
    if (currentFocus) params.focus = currentFocus
    if (dependencyHops === dependentHops) {
      if (dependencyHops !== 1) params.hops = String(dependencyHops)
    } else {
      params.dependencies = String(dependencyHops)
      params.dependents = String(dependentHops)
    }
    if (groupIntermediate) params.group = 'intermediate'
    setSearchParams(params, { replace: true })
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [tokens, currentFocus, dependencyHops, dependentHops, groupIntermediate])

  // Permission rows share the graph's selection, but omit retained connectors
  // and focus nodes that do not satisfy the active filters.
  const visibleIds = new Set(nodeIds)
  const grantMatches = hasGranteeFilter
    ? filtered.filter((node) => visibleIds.has(node.id)).map((node) => ({ node, grants: matchingGrants(node, tokens) }))
    : []
  const grantRows: ObjectGrantRow[] = grantMatches.flatMap(({ node, grants }) =>
    grants.map((grant) => ({ node, grant })),
  )
  const totalGrants = grantRows.length

  function copyShareableLink() {
    navigator.clipboard
      .writeText(window.location.href)
      .then(() => {
        setCopied(true)
        setTimeout(() => setCopied(false), 1500)
      })
      .catch(() => {})
  }

  if (!index) return null

  function drillInto(id: string) {
    // Catalog object filters locate a starting object; carrying them into its
    // neighborhood can hide every caller and target (for example Type is
    // LinkedServers). Keep content/access investigations and filters explicitly
    // applied while already navigating a neighborhood.
    if (!currentFocus)
      setTokens((prev) => prev.filter((token) => token.attribute === 'grantee' || token.attribute === 'ddl'))
    setFocusStack((prev) => (prev[prev.length - 1] === id ? prev : [...prev, id]))
  }

  function goToBreadcrumb(i: number) {
    setFocusStack((prev) => prev.slice(0, i + 1))
  }

  // Releasing focus keeps you on the object you were looking at rather than
  // dumping you into the whole catalog - by turning the navigation into a real
  // filter token at exactly the moment it stops being navigation. (Seeding
  // that token up front instead is what used to break every filter added
  // while navigating; see the tokens initializer.)
  function clearFocus() {
    const node = currentFocus ? index?.byId.get(currentFocus) : undefined
    if (node && !tokens.some((t) => t.attribute === 'name' && t.values.includes(node.qualifiedName))) {
      setTokens([...tokens, { id: newTokenId(), attribute: 'name', operator: 'is', values: [node.qualifiedName] }])
    }
    setFocusStack([])
  }

  return (
    <div className="page page--wide lineage-page">
      <div className="lineage-header-row">
        <h1 className="page-title">
          Lineage explorer
          <HelpButton topic="lineage" />
        </h1>
        <button type="button" className="lineage-share-btn" onClick={copyShareableLink}>
          {copied ? 'Copied!' : 'Copy link'}
        </button>
      </div>

      <section aria-label="Lineage filters">
        <FilterBar
          nodes={allNodes}
          tokens={tokens}
          onChange={setTokens}
          placeholder={
            currentFocus
              ? 'Filter what surrounds this object... (including grantee or DDL content)'
              : 'Filter the graph... (server, database, schema, type, name, grantee or DDL content)'
          }
        />
        <p className="muted">
          Combine object, grantee and DDL content filters. Use Grantee is for an exact user, role or group; Grantee
          contains for a partial name.
        </p>
      </section>

      {hasGranteeFilter && (
        <section aria-label="Matching permissions">
          <p className="muted">
            {totalGrants} grant{totalGrants === 1 ? '' : 's'} across {grantMatches.length} object
            {grantMatches.length === 1 ? '' : 's'} matching all filters. Recorded permissions include GRANT and DENY;
            connecting graph objects do not imply access.
          </p>
          {grantMatches.length > 0 && (
            <>
              <div className="lineage-header-row" style={{ margin: '0.5rem 0' }}>
                <span className="muted">One row per object, one CSV row per permission.</span>
                <CsvExportButton
                  rows={grantRows}
                  columns={objectGrantColumns}
                  filename={csvFileName('syncsql-access', 'filtered')}
                />
              </div>
              <div className="explorer-table-wrap" style={{ marginBottom: '0.75rem' }}>
                <table className="explorer-table">
                  <thead>
                    <tr>
                      <th>Object</th>
                      <th>Type</th>
                      <th>Server</th>
                      <th>Database</th>
                      <th>Permissions</th>
                    </tr>
                  </thead>
                  <tbody>
                    {grantMatches.map(({ node, grants }) => (
                      <tr key={node.id}>
                        <td>
                          <Link to={`/object/${node.id}`}>{node.qualifiedName}</Link>
                        </td>
                        <td>
                          <TypeBadge type={node.type} />
                        </td>
                        <td>{node.server}</td>
                        <td>{node.database}</td>
                        <td>
                          <span className="column-tags">
                            {grants.map((g, i) => (
                              <span
                                key={`${g.grantee}-${g.permission}-${g.column ?? ''}-${i}`}
                                className={g.state === 'DENY' ? 'column-tag column-tag--deny' : 'column-tag'}
                              >
                                {g.permission}
                                {g.column ? `(${g.column})` : ''}
                                {g.state === 'DENY' ? ' [DENY]' : ''}
                              </span>
                            ))}
                          </span>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </>
          )}
        </section>
      )}

      {currentFocus ? (
        <div className="lineage-nav">
          <span className="lineage-nav-label">Navigating:</span>
          <ol className="breadcrumb-trail">
            {focusStack.map((id, i) => {
              const n = index.byId.get(id)
              const isLast = i === focusStack.length - 1
              return (
                <li key={`${id}-${i}`}>
                  {isLast ? (
                    <span className="breadcrumb-current">{n?.qualifiedName ?? id}</span>
                  ) : (
                    <button type="button" className="breadcrumb-link" onClick={() => goToBreadcrumb(i)}>
                      {n?.qualifiedName ?? id}
                    </button>
                  )}
                  {!isLast && <span className="breadcrumb-sep">&rsaquo;</span>}
                </li>
              )
            })}
          </ol>
          <button
            type="button"
            className="lineage-nav-back"
            onClick={() => setFocusStack((prev) => prev.slice(0, -1))}
            disabled={focusStack.length <= 1}
          >
            &larr; Back
          </button>
          <button type="button" className="lineage-nav-clear" onClick={clearFocus}>
            Clear focus
          </button>
        </div>
      ) : null}

      {currentFocus ? (
        <p className="muted" style={{ margin: '0.5rem 0' }}>
          {nodeIds.length < narrowedFrom
            ? `Showing ${nodeIds.length} of ${narrowedFrom} objects around ${index.byId.get(currentFocus)?.qualifiedName ?? currentFocus} - the filters above narrow this neighborhood, not the whole catalog.`
            : `${nodeIds.length} object(s) around ${index.byId.get(currentFocus)?.qualifiedName ?? currentFocus}. Filter above to narrow this neighborhood, or clear the focus to search the whole catalog.`}
        </p>
      ) : (
        <p className="muted" style={{ margin: '0.5rem 0' }}>
          {nodeIds.length} object(s) shown.{' '}
          {tokens.length === 0 && 'Start typing to narrow this down by server, database, schema or type. '}
          {nodeIds.length > 0 &&
            'Click any node to drill into its own dependencies/dependents; double-click to open its full detail page.'}
        </p>
      )}

      {connectorIds.length > 0 && (
        <p className="muted lineage-connector-hint">
          {connectorIds.length} connecting object{connectorIds.length === 1 ? '' : 's'} kept outside the filters to
          preserve paths to matching objects.
        </p>
      )}

      {currentFocus && nodeIds.length === 1 && narrowedFrom > 1 && (
        <p className="muted">
          Filters hide all surrounding objects.{' '}
          <button type="button" className="lineage-nav-back" onClick={() => setTokens([])}>
            Show full neighborhood
          </button>
        </p>
      )}

      <div className="investigation-layout">
        <div className="workspace-content graph-workspace">
          {nodeIds.length > GRAPH_CAP ? (
            <SelectionBreakdown
              nodes={nodeIds.map((id) => index.byId.get(id)).filter((n): n is CatalogNode => Boolean(n))}
              onNarrow={(attribute, value) => {
                setTokens([...tokens, { id: newTokenId(), attribute, operator: 'is', values: [value] }])
              }}
            />
          ) : (
            <>
              <LineageGraph
                nodeIds={nodeIds}
                focusId={currentFocus}
                height="70vh"
                onNodeActivate={drillInto}
                onEdgeInspect={setEdgeEvidence}
                groupIntermediate={groupIntermediate}
                connectorIds={connectorIds}
              />
              <details className="lineage-legend">
                <summary>
                  <span>Graph legend</span>
                  <span className="legend-reminder">→ References · dashed = dynamic SQL</span>
                </summary>
                <ul>
                  {GRAPH_LEGEND_ITEMS.map((item) => (
                    <li key={item.kind}>
                      <span
                        className={
                          item.kind === 'arrow'
                            ? 'legend-arrow'
                            : item.kind === 'focus' || item.kind === 'group'
                              ? `legend-node legend-node--${item.kind}`
                              : `legend-line legend-line--${item.kind}`
                        }
                        aria-hidden="true"
                      >
                        {item.kind === 'arrow' ? '→' : null}
                      </span>
                      {item.label}
                    </li>
                  ))}
                </ul>
                <p className="muted">{GRAPH_LEGEND_NOTE}</p>
                <ul aria-label="Object types in this graph">
                  {[
                    ...new Set(
                      nodeIds.map((id) => index.byId.get(id)?.type).filter((type): type is string => Boolean(type)),
                    ),
                  ]
                    .sort()
                    .map((type) => (
                      <li key={type}>
                        <span className="legend-node" style={{ borderColor: colorForType(type) }} aria-hidden="true" />
                        {type}
                      </li>
                    ))}
                </ul>
              </details>
            </>
          )}
        </div>
        <div className="lineage-sidebar">
          {currentFocus && (
            <details className="lineage-view-options" open>
              <summary>View options</summary>
              <div className="lineage-view-controls">
                {(
                  [
                    ['Dependencies', dependencyHops, setDependencyHops],
                    ['Dependents', dependentHops, setDependentHops],
                  ] as const
                ).map(([label, value, setValue]) => (
                  <div className="lineage-hop-control" key={label}>
                    <label className="lineage-hops" htmlFor={`lineage-${label.toLowerCase()}-hops`}>
                      {label}
                    </label>
                    <select
                      id={`lineage-${label.toLowerCase()}-hops`}
                      aria-label={`${label} hops`}
                      value={value}
                      onChange={(event) => setValue(Number(event.target.value))}
                    >
                      {HOP_OPTIONS.map((hop) => (
                        <option key={hop} value={hop}>
                          {hop} hop{hop === 1 ? '' : 's'}
                        </option>
                      ))}
                    </select>
                    <button
                      type="button"
                      className="lineage-nav-back"
                      disabled={value >= MAX_HOPS}
                      onClick={() => setValue(value + 1)}
                      aria-label={`Add ${label.toLowerCase()} hop`}
                    >
                      + Add hop
                    </button>
                  </div>
                ))}
                <label className="lineage-group-toggle">
                  <input
                    type="checkbox"
                    checked={groupIntermediate}
                    onChange={(event) => setGroupIntermediate(event.target.checked)}
                  />
                  Group intermediate layers
                </label>
              </div>
              <p className="muted">
                Dependencies are objects the focus references; dependents are objects that reference it. Set either
                direction to 0 to hide it. Grouping combines intermediate objects of the same type and hop, keeping the
                focus and outer objects visible.
              </p>
            </details>
          )}
          <InspectorPanel>
            {edgeEvidence && nodeIds.length <= GRAPH_CAP && (
              <section aria-label="Edge evidence">
                <h3>Edge evidence</h3>
                <p>
                  {index.byId.get(edgeEvidence.from)?.qualifiedName ?? edgeEvidence.from} →{' '}
                  {index.byId.get(edgeEvidence.to)?.qualifiedName ?? edgeEvidence.to}
                </p>
                <p className="muted">
                  Known column references detected in DDL; best-effort evidence, not certified column-level lineage.
                </p>
                <div className="column-tags">
                  {edgeEvidence.columns.map((column) => (
                    <span key={column} className="column-tag">
                      {column}
                    </span>
                  ))}
                </div>
                {edgeEvidence.dynamic && <p className="muted">Recovered from dynamically built SQL.</p>}
              </section>
            )}
            {currentFocus && index.byId.has(currentFocus) ? (
              <>
                <h3 className="inspector-identity">{index.byId.get(currentFocus)!.qualifiedName}</h3>
                <TypeBadge type={index.byId.get(currentFocus)!.type} />
                <p className="muted">
                  {index.byId.get(currentFocus)!.server} → {index.byId.get(currentFocus)!.database}
                </p>
                <Link to={`/object/${currentFocus}`}>Open object workbench →</Link>
                <RelatedObjects
                  title="Used by"
                  rootId={currentFocus}
                  ids={index.incoming.get(currentFocus) ?? []}
                  direction="incoming"
                />
                <RelatedObjects
                  title="Depends on"
                  rootId={currentFocus}
                  ids={index.outgoing.get(currentFocus) ?? []}
                  direction="outgoing"
                />
                {hasGranteeFilter && (
                  <>
                    <h3>Recorded permissions</h3>
                    <p className="muted">Current object grants, not an effective-access calculation.</p>
                    {index.byId.get(currentFocus)!.grants.length === 0 && <p>No grants recorded.</p>}
                    <ul className="permission-list">
                      {index.byId.get(currentFocus)!.grants.map((grant, i) => (
                        <li key={i}>
                          <button
                            type="button"
                            className="breadcrumb-link"
                            onClick={() => {
                              setFocusStack([])
                              setTokens([
                                ...tokens.filter((token) => token.attribute !== 'grantee'),
                                { id: newTokenId(), attribute: 'grantee', operator: 'is', values: [grant.grantee] },
                              ])
                            }}
                          >
                            {grant.grantee}
                          </button>
                          <span className={`grant-state grant-state--${grant.state === 'DENY' ? 'deny' : 'grant'}`}>
                            {grant.state}
                          </span>{' '}
                          {grant.permission} · {grant.column ?? 'whole object'}
                        </li>
                      ))}
                    </ul>
                  </>
                )}
              </>
            ) : (
              <p className="empty-state">
                Drill into a graph node to inspect its identity and relationships. Edge details appear beside the
                selected relationship in the graph.
              </p>
            )}
          </InspectorPanel>
        </div>
      </div>
    </div>
  )
}

/** The grouping axes offered as a way out of an over-sized selection - the ones a filter token can narrow on. */
const BREAKDOWN_AXES: { by: GroupBy; attribute: 'server' | 'database' | 'type'; label: string }[] = [
  { by: 'server', attribute: 'server', label: 'By server' },
  { by: 'database', attribute: 'database', label: 'By database' },
  { by: 'type', attribute: 'type', label: 'By type' },
]

/** Rows past this per axis are summarized as "+N more" - the point is to pick one, not to read them all. */
const BREAKDOWN_ROWS = 8

/**
 * What a too-large selection is actually made of, with every row a one-click
 * narrowing. Drawing 4,000 nodes helps nobody, but "3,812 objects: 3,100 of them
 * Tables in WarehouseDb" is the answer to the question that got someone here -
 * and clicking that row is the next step, instead of a warning that just stops.
 */
function SelectionBreakdown({
  nodes,
  onNarrow,
}: {
  nodes: CatalogNode[]
  onNarrow?: (attribute: 'server' | 'database' | 'type', value: string) => void
}) {
  return (
    <div className="lineage-warning selection-breakdown">
      <p>
        <strong>{nodes.length} objects</strong> match this selection - too many to draw as one graph, and unreadable if
        we did. Here is what they are
        {onNarrow ? '; pick a row to narrow the filter, or drill into a single object from the Explorer.' : '.'}
      </p>
      <div className="selection-breakdown-axes">
        {BREAKDOWN_AXES.map(({ by, attribute, label }) => {
          const groups = groupRelated(nodes, by)
          const shown = groups.slice(0, BREAKDOWN_ROWS)
          const rest = groups.length - shown.length
          return (
            <div key={by} className="selection-breakdown-axis">
              <h4>{label}</h4>
              <ul>
                {shown.map((group) =>
                  onNarrow ? (
                    <li key={group.key}>
                      <button
                        type="button"
                        className="selection-breakdown-row"
                        onClick={() => onNarrow(attribute, group.key)}
                      >
                        <span className="selection-breakdown-name">{group.key}</span>
                        <span className="selection-breakdown-count">{group.nodes.length}</span>
                      </button>
                    </li>
                  ) : (
                    <li key={group.key}>
                      <span className="selection-breakdown-row selection-breakdown-row--static">
                        <span className="selection-breakdown-name">{group.key}</span>
                        <span className="selection-breakdown-count">{group.nodes.length}</span>
                      </span>
                    </li>
                  ),
                )}
                {rest > 0 && (
                  <li className="muted selection-breakdown-rest">
                    +{rest} more {by}
                    {rest === 1 ? '' : 's'}
                  </li>
                )}
              </ul>
            </div>
          )
        })}
      </div>
    </div>
  )
}
