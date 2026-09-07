import { useEffect, useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import { useCatalog } from '../lib/CatalogContext'
import TypeBadge from './TypeBadge'
import { colorForType } from '../lib/typeColors'
import { getEdgeColumns, isDynamicEdge } from '../lib/neighborhood'
import { countByType, groupRelated, resolveNodes, searchNodes, GROUP_BY_OPTIONS, type GroupBy } from '../lib/grouping'
import type { CatalogNode } from '../types'

/**
 * Up to this many related objects, a flat list is the clearest thing there is -
 * no controls, no groups, nothing to click before you can read it.
 */
const FLAT_LIMIT = 12

/** Rows rendered per group before "Show all" - enough to recognize the group, short enough to scroll past. */
const ROWS_PER_GROUP = 25

/** Groups are expanded from the top until this many rows are on screen, so the page opens on something readable. */
const AUTO_EXPAND_ROWS = 30

interface RelatedObjectsProps {
  title: string
  rootId: string
  ids: readonly string[]
  direction: 'outgoing' | 'incoming'
}

/**
 * One side of an object's lineage - "Depends on" or "Used by".
 *
 * A handful of dependencies renders as the plain list it always was. A hub with
 * hundreds of them switches to a summary-first view instead: what the set is
 * made of (counts per type), a search box, and collapsed groups - because a flat
 * list of 300 links answers no question anybody actually has about a hub object.
 */
export default function RelatedObjects({ title, rootId, ids, direction }: RelatedObjectsProps) {
  const { index } = useCatalog()
  const nodes = useMemo(() => (index ? resolveNodes(index, ids) : []), [index, ids])

  const [groupBy, setGroupBy] = useState<GroupBy>('type')
  const [query, setQuery] = useState('')
  const [typeFilter, setTypeFilter] = useState<string | null>(null)

  // A different object under the same component instance (in-page navigation)
  // must not inherit the previous one's search and type filter.
  useEffect(() => {
    setQuery('')
    setTypeFilter(null)
  }, [rootId])

  const typeCounts = useMemo(() => countByType(nodes), [nodes])
  const matches = useMemo(() => {
    const byType = typeFilter ? nodes.filter((node) => node.type === typeFilter) : nodes
    return searchNodes(byType, query)
  }, [nodes, typeFilter, query])
  const groups = useMemo(() => groupRelated(matches, groupBy), [matches, groupBy])

  if (!index) return null

  const heading = (
    <h3>
      {title} ({nodes.length})
    </h3>
  )

  if (nodes.length === 0) {
    return (
      <div>
        {heading}
        <p className="muted">None found.</p>
      </div>
    )
  }

  if (nodes.length <= FLAT_LIMIT) {
    return (
      <div>
        {heading}
        <ul className="related-list">
          {nodes.map((node) => (
            <RelatedRow key={node.id} node={node} rootId={rootId} direction={direction} />
          ))}
        </ul>
      </div>
    )
  }

  let rowsSoFar = 0
  return (
    <div className="related-panel">
      {heading}

      <div className="related-summary">
        {typeCounts.map(({ type, count }) => {
          const active = typeFilter === type
          return (
            <button
              key={type}
              type="button"
              className={active ? 'related-type-chip related-type-chip--active' : 'related-type-chip'}
              style={{ borderLeftColor: colorForType(type) }}
              aria-pressed={active}
              // Spelled out rather than left to the visible "Tables 40": the
              // group headers below read the same way, and a screen reader
              // shouldn't have to guess which one filters.
              aria-label={active ? `Clear the ${type} filter` : `Filter to ${type} (${count})`}
              onClick={() => setTypeFilter(active ? null : type)}
            >
              {type} <span className="related-type-chip-count">{count}</span>
            </button>
          )
        })}
      </div>

      <div className="related-controls">
        <input
          type="search"
          className="related-search"
          value={query}
          placeholder={`Search these ${nodes.length} objects...`}
          aria-label={`Search ${title.toLowerCase()} objects`}
          onChange={(event) => setQuery(event.target.value)}
        />
        <label className="related-groupby">
          Group by
          <select value={groupBy} onChange={(event) => setGroupBy(event.target.value as GroupBy)}>
            {GROUP_BY_OPTIONS.map((option) => (
              <option key={option.value} value={option.value}>
                {option.label}
              </option>
            ))}
          </select>
        </label>
      </div>

      {matches.length === 0 ? (
        <p className="muted">
          Nothing here matches that filter.{' '}
          <button
            type="button"
            className="related-reset"
            onClick={() => {
              setQuery('')
              setTypeFilter(null)
            }}
          >
            Reset
          </button>
        </p>
      ) : (
        <>
          <p className="muted related-count-line">
            Showing {matches.length} of {nodes.length} - grouped into {groups.length} {groups.length === 1 ? 'group' : 'groups'}.
          </p>
          {groups.map((group) => {
            const open = rowsSoFar < AUTO_EXPAND_ROWS
            rowsSoFar += group.nodes.length
            return (
              <RelatedGroupSection
                key={`${groupBy}:${group.key}`}
                label={group.key}
                nodes={group.nodes}
                rootId={rootId}
                direction={direction}
                defaultOpen={open}
              />
            )
          })}
        </>
      )}
    </div>
  )
}

function RelatedGroupSection({
  label,
  nodes,
  rootId,
  direction,
  defaultOpen,
}: {
  label: string
  nodes: CatalogNode[]
  rootId: string
  direction: 'outgoing' | 'incoming'
  defaultOpen: boolean
}) {
  const [showAll, setShowAll] = useState(false)
  // Deliberately a controlled disclosure rather than <details>: which groups
  // start open depends on the current filter, and a native <details> keeps
  // whatever state the browser last put it in.
  const [open, setOpen] = useState(defaultOpen)
  useEffect(() => setOpen(defaultOpen), [defaultOpen])

  const shown = showAll ? nodes : nodes.slice(0, ROWS_PER_GROUP)
  const hidden = nodes.length - shown.length

  return (
    <div className="related-group">
      <button type="button" className="related-group-summary" aria-expanded={open} onClick={() => setOpen((v) => !v)}>
        <span>
          <span className="related-group-caret" aria-hidden="true">
            {open ? '▾' : '▸'}
          </span>{' '}
          {label}
        </span>
        <span className="related-group-count">{nodes.length}</span>
      </button>
      {open && (
        <>
          <ul className="related-list">
            {shown.map((node) => (
              <RelatedRow key={node.id} node={node} rootId={rootId} direction={direction} />
            ))}
          </ul>
          {hidden > 0 && (
            <button type="button" className="related-more" onClick={() => setShowAll(true)}>
              Show all {nodes.length}
            </button>
          )}
          {showAll && nodes.length > ROWS_PER_GROUP && (
            <button type="button" className="related-more" onClick={() => setShowAll(false)}>
              Show fewer
            </button>
          )}
        </>
      )}
    </div>
  )
}

function RelatedRow({ node, rootId, direction }: { node: CatalogNode; rootId: string; direction: 'outgoing' | 'incoming' }) {
  const { index } = useCatalog()
  if (!index) return null
  const [from, to] = direction === 'outgoing' ? [rootId, node.id] : [node.id, rootId]
  const columns = getEdgeColumns(index, from, to)
  return (
    <li className="related-object-row">
      <span className="reference-direction" role="img" aria-label={direction === 'outgoing' ? 'This object references' : 'References this object'} title={direction === 'outgoing' ? 'This object → referenced object' : 'This object ← referencing object'}>{direction === 'outgoing' ? '→' : '←'}</span>
      <Link to={`/object/${node.id}`}>{node.qualifiedName}</Link> <TypeBadge type={node.type} />
      {isDynamicEdge(index, from, to) && (
        <span
          className="column-tag column-tag--dynamic"
          title="Found in SQL built as a string at runtime (OPENQUERY, EXEC of a string) rather than read off the parse tree - a weaker signal than an ordinary reference."
        >
          dynamic
        </span>
      )}
      {columns.length > 0 && <ColumnTags columns={columns} />}
    </li>
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
