import { useMemo, useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import { useCatalog } from '../lib/CatalogContext'
import LineageGraph from '../components/LineageGraph'

const ALL = '__all__'

export default function LineagePage() {
  const { index } = useCatalog()
  const [searchParams] = useSearchParams()
  const focusId = searchParams.get('focus') ?? undefined

  const [server, setServer] = useState(ALL)
  const [database, setDatabase] = useState(ALL)

  const databases = useMemo(() => {
    if (!index) return []
    const found = index.tree.find((s) => s.name === server)
    return found ? found.databases.map((d) => d.name) : []
  }, [index, server])

  const nodeIds = useMemo(() => {
    if (!index) return []
    return index.catalog.nodes
      .filter((n) => server === ALL || n.server === server)
      .filter((n) => database === ALL || n.database === database)
      .map((n) => n.id)
  }, [index, server, database])

  if (!index) return null

  return (
    <div className="page page--wide">
      <h1>Lineage explorer</h1>
      <div className="lineage-filters">
        <label>
          Server
          <select
            value={server}
            onChange={(e) => {
              setServer(e.target.value)
              setDatabase(ALL)
            }}
          >
            <option value={ALL}>All servers</option>
            {index.catalog.servers.map((s) => (
              <option key={s} value={s}>
                {s}
              </option>
            ))}
          </select>
        </label>
        <label>
          Database
          <select value={database} onChange={(e) => setDatabase(e.target.value)} disabled={server === ALL}>
            <option value={ALL}>All databases</option>
            {databases.map((d) => (
              <option key={d} value={d}>
                {d}
              </option>
            ))}
          </select>
        </label>
        <span className="muted">{nodeIds.length} object(s) shown</span>
      </div>

      {nodeIds.length > 300 ? (
        <p className="lineage-warning">
          {nodeIds.length} objects match this filter - narrow it down by server/database to keep the graph readable.
        </p>
      ) : (
        <LineageGraph nodeIds={nodeIds} focusId={focusId} height="70vh" />
      )}
    </div>
  )
}
