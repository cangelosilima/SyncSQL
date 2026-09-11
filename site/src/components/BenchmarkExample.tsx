import { useState } from 'react'
import { Link } from 'react-router-dom'
import type { Catalog } from '../types'

export default function BenchmarkExample({ catalog }: { catalog: Catalog }) {
  const [user, setUser] = useState('')
  const example = catalog.example
  if (!example) return null
  const nodes = new Map(catalog.nodes.map(node => [node.id, node]))
  return (
    <section className="benchmark-example" aria-labelledby="benchmark-title">
      <div className="benchmark-heading">
        <div>
          <span className="sync-line-badge">Interactive example · synthetic data</span>
          <h2 id="benchmark-title">{example.title}</h2>
          <p>Explore one Oracle server, two SQL Servers, {catalog.nodes.length} objects and {example.users.length} workload users.</p>
        </div>
        <Link className="ai-primary-link" to="/explorer">Explore all objects</Link>
      </div>
      <p className="muted">This snapshot comes from a passing Docker extraction benchmark. Oracle → SQL Server gateway reads, including a SQL linked-server hop, were executed. SQL Server → Oracle references and round trips are lineage examples; Linux SQL Server cannot execute the Oracle links. Recorded grants describe explicit permissions, not effective access through remote logins.</p>
      <div className="benchmark-user">
        <label htmlFor="benchmark-user">Investigate a user</label>
        <select id="benchmark-user" value={user} onChange={event => setUser(event.target.value)}>
          <option value="">Choose a workload user…</option>
          {example.users.map(principal => <option key={principal.name} value={principal.name}>{principal.name} · {principal.server} / {principal.database}</option>)}
        </select>
        {user && <Link to={`/lineage?tab=access&grantee=${encodeURIComponent(user)}&exact=1`}>View recorded access</Link>}
      </div>
      <h3>Follow a benchmark path</h3>
      <p className="muted">Expand a path to see every hop, including cycles. Open any object to inspect its SQL, permissions and local lineage.</p>
      <div className="benchmark-paths">
        {example.paths.map(route => <details key={route.name}>
          <summary>{route.name.replace(/>/g, '→')}</summary>
          <ol>{route.nodes.map((id, i) => {
            const node = nodes.get(id)
            return <li key={`${i}:${id}`}>
              <Link to={`/object/${id}`}>{node?.qualifiedName ?? id}</Link>
              <span className="muted">{node?.server} / {node?.database} · {node?.type}</span>
            </li>
          })}</ol>
          <Link to={`/lineage?focus=${encodeURIComponent(route.nodes[0])}&dependencies=6&dependents=0`}>Inspect starting object’s lineage</Link>
        </details>)}
      </div>
    </section>
  )
}
