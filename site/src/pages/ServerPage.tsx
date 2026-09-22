import { useMemo } from 'react'
import { Link, useParams } from 'react-router-dom'
import { useCatalog } from '../lib/CatalogContext'
import { useCatalogSelection } from '../lib/useCatalogData'
import CatalogLoadStatus from '../components/CatalogLoadStatus'
import type { CatalogLinkedServerReference } from '../types'
import type { CatalogIndex } from '../lib/catalog'

function unique<T>(values: T[]): T[] {
  return [...new Set(values)]
}

export default function ServerPage() {
  const { index: baseIndex } = useCatalog()
  const { serverName = '' } = useParams()
  const server = serverName
  const nodes = useMemo(
    () => baseIndex?.catalog.nodes.filter((node) => node.server.toLowerCase() === server.toLowerCase()) ?? [],
    [baseIndex, server],
  )
  const { index, loading, error } = useCatalogSelection(baseIndex, { ids: nodes.map((node) => node.id) })
  if (loading || error)
    return (
      <div className="page">
        <CatalogLoadStatus loading={loading} error={error} />
      </div>
    )
  if (!index) return null
  const detail = index.catalog.serverDetails?.find((item) => item.name.toLowerCase() === server.toLowerCase())
  const outgoingLinks = nodes.filter((node) => node.type === 'LinkedServers' || node.type === 'DatabaseLinks')
  const refs = index.catalog.linkedServerReferences ?? []
  const outgoing = refs.filter((ref) => outgoingLinks.some((link) => link.id === ref.linkedServer))
  const incoming = refs.filter(
    (ref) =>
      ref.to &&
      index.byId.get(ref.to)?.server.toLowerCase() === server.toLowerCase() &&
      index.byId.get(ref.from)?.server.toLowerCase() !== server.toLowerCase(),
  )
  const topology = unique(
    [
      ...outgoing.map((ref) => (ref.to ? index.byId.get(ref.to)?.server : undefined)),
      ...incoming.map((ref) => index.byId.get(ref.from)?.server),
    ].filter((value): value is string => typeof value === 'string' && value.toLowerCase() !== server.toLowerCase()),
  )
  const databaseNames = unique(
    nodes.filter((node) => node.database !== '_ServerLevel').map((node) => node.database),
  ).sort()
  return (
    <div className="page page--wide server-workbench">
      <p className="breadcrumb">
        <Link to="/explorer">Explorer</Link> / {server}
      </p>
      <h1 className="page-title">Server explorer</h1>
      <div className="server-hero">
        <div>
          <p className="server-kicker">Server</p>
          <h2>{server}</h2>
          <p className="server-hostname">{detail?.hostname ?? 'Hostname not recorded'}</p>
        </div>
        <div className="server-facts">
          <span>
            <small>Environment</small>
            {detail?.environment ?? 'Unclassified'}
          </span>
          <span>
            <small>Databases</small>
            {databaseNames.length}
          </span>
          <span>
            <small>Objects</small>
            {nodes.length}
          </span>
        </div>
      </div>
      <div className="server-tags" aria-label="Server tags">
        {(detail?.tags ?? []).map((tag) => (
          <span className="server-tag" key={tag}>
            {tag}
          </span>
        ))}
        {(detail?.tags ?? []).length === 0 && <span className="muted">No custom tags</span>}
      </div>
      <section className="server-section" aria-labelledby="server-topology-title">
        <h2 id="server-topology-title">Server topology</h2>
        <p className="muted">Other servers connected through linked servers and database links.</p>
        {topology.length === 0 ? (
          <p className="empty-state">No cross-server relationships are recorded.</p>
        ) : (
          <div className="server-topology">
            <div className="server-topology-node server-topology-node--current">
              {server}
              <small>current server</small>
            </div>
            {topology.map((name) => (
              <Link className="server-topology-node" key={name} to={`/server/${encodeURIComponent(name)}`}>
                {name}
                <small>connected server</small>
              </Link>
            ))}
          </div>
        )}
      </section>
      <section className="server-section" aria-labelledby="server-links-title">
        <h2 id="server-links-title">Database links</h2>
        <div className="server-link-columns">
          <RelationshipList title="References to" refs={outgoing} index={index} direction="outgoing" />
          <RelationshipList title="Referenced by" refs={incoming} index={index} direction="incoming" />
        </div>
      </section>
    </div>
  )
}

function RelationshipList({
  title,
  refs,
  index,
  direction,
}: {
  title: string
  refs: CatalogLinkedServerReference[]
  index: CatalogIndex
  direction: 'incoming' | 'outgoing'
}) {
  const rows = unique(
    refs.map((ref) => {
      const objectId = direction === 'incoming' ? ref.from : ref.to
      return `${objectId ?? ''}|${ref.database ?? ''}|${ref.schema ?? ''}|${ref.name}`
    }),
  ).map((key) => {
    const [objectId, database, schema, name] = key.split('|')
    return { objectId, database, schema, name }
  })
  return (
    <div className="server-link-list">
      <h3>{title}</h3>
      {rows.length === 0 ? (
        <p className="muted">None recorded.</p>
      ) : (
        <ul>
          {rows.map((row) => (
            <li key={`${row.objectId}-${row.name}`}>
              {row.objectId && index.byId.has(row.objectId) ? (
                <Link to={`/object/${row.objectId}`}>{index.byId.get(row.objectId)!.qualifiedName}</Link>
              ) : (
                [row.database, row.schema, row.name].filter(Boolean).join('.')
              )}
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}
