import { useMemo, useState } from 'react'
import { Link, useSearchParams } from 'react-router-dom'
import { useCatalog } from '../lib/CatalogContext'
import TypeBadge from '../components/TypeBadge'
import { findObjectsForGrantee, getSuggestedGrantees } from '../lib/grants'
import { useDebouncedValue } from '../lib/useDebouncedValue'

export default function Access() {
  const { index } = useCatalog()
  const [searchParams, setSearchParams] = useSearchParams()
  const [query, setQuery] = useState(searchParams.get('grantee') ?? '')
  const [exact, setExact] = useState(false)
  const [suggestionsOpen, setSuggestionsOpen] = useState(false)

  const debouncedQuery = useDebouncedValue(query, 120)
  const nodes = index?.catalog.nodes ?? []

  const suggestions = useMemo(() => (query.trim() ? getSuggestedGrantees(nodes, query, 20) : []), [nodes, query])

  const matches = useMemo(() => findObjectsForGrantee(nodes, debouncedQuery, exact), [nodes, debouncedQuery, exact])

  const totalGrants = matches.reduce((sum, m) => sum + m.grants.length, 0)

  function pickGrantee(value: string) {
    setQuery(value)
    setSearchParams(value ? { grantee: value } : {})
    setSuggestionsOpen(false)
  }

  return (
    <div className="page page--wide">
      <h1>Access</h1>
      <p className="muted">
        Search by grantee (user, role or group) to see every object they have a GRANT or DENY permission on, down to
        the column when the grant was scoped that way.
      </p>

      <div className="access-search">
        <div className="filter-bar" style={{ margin: 0, flex: 1 }}>
          <div className="filter-bar-input-row">
            <input
              type="text"
              className="filter-bar-input"
              placeholder="Search grantee (user, role, group)..."
              value={query}
              onChange={(e) => {
                setQuery(e.target.value)
                setSuggestionsOpen(true)
                setSearchParams(e.target.value ? { grantee: e.target.value } : {})
              }}
              onFocus={() => setSuggestionsOpen(true)}
              onBlur={() => setTimeout(() => setSuggestionsOpen(false), 150)}
            />
          </div>
          {suggestionsOpen && suggestions.length > 0 && (
            <ul className="filter-suggestions" role="listbox">
              {suggestions.map((s) => (
                <li key={s} role="option" aria-selected={false} className="filter-suggestion" onMouseDown={(e) => { e.preventDefault(); pickGrantee(s) }}>
                  {s}
                </li>
              ))}
            </ul>
          )}
        </div>
        <label className="access-exact-toggle">
          <input type="checkbox" checked={exact} onChange={(e) => setExact(e.target.checked)} />
          Exact match
        </label>
      </div>

      {!debouncedQuery.trim() ? (
        <p className="muted" style={{ marginTop: '1rem' }}>
          Start typing a grantee name to search.
        </p>
      ) : matches.length === 0 ? (
        <p className="muted" style={{ marginTop: '1rem' }}>
          No grants found for &quot;{debouncedQuery}&quot;.
        </p>
      ) : (
        <>
          <p className="muted" style={{ margin: '1rem 0 0.5rem' }}>
            {totalGrants} grant{totalGrants === 1 ? '' : 's'} across {matches.length} object{matches.length === 1 ? '' : 's'} matching &quot;
            {debouncedQuery}&quot;.
          </p>
          <div className="explorer-table-wrap">
            <table className="explorer-table">
              <thead>
                <tr>
                  <th>Object</th>
                  <th>Type</th>
                  <th>Server</th>
                  <th>Database</th>
                  <th>Grantee</th>
                  <th>Permissions</th>
                </tr>
              </thead>
              <tbody>
                {matches.map(({ node, grants }) => (
                  <tr key={node.id}>
                    <td>
                      <Link to={`/object/${node.id}`}>{node.qualifiedName}</Link>
                    </td>
                    <td>
                      <TypeBadge type={node.type} />
                    </td>
                    <td>{node.server}</td>
                    <td>{node.database}</td>
                    <td>{[...new Set(grants.map((g) => g.grantee))].join(', ')}</td>
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
    </div>
  )
}
