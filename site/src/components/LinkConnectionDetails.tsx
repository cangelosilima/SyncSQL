import { Link } from 'react-router-dom'
import type { CatalogNode } from '../types'

export default function LinkConnectionDetails({ node }: { node: CatalogNode }) {
  const link = node.link
  if (!link && !node.principal) return null
  return (
    <section className="object-section" aria-label="Connection context">
      <h2>{link ? 'Connection destination' : 'Principal context'}</h2>
      {link && (
        <>
          <dl>
            <dt>Connect identifier</dt>
            <dd>{link.connectIdentifier ?? 'Not recorded'}</dd>
            <dt>Destination</dt>
            <dd>
              {link.dataSource ?? 'Endpoint not resolved'}
              {link.targetEngine && ` (${link.targetEngine})`}
            </dd>
            <dt>Database / default schema</dt>
            <dd>
              {link.database ?? 'Not resolved'} / {link.defaultSchema ?? 'Not resolved'}
            </dd>
            {(link.gatewayHost || link.gatewaySid) && (
              <>
                <dt>Gateway</dt>
                <dd>
                  {link.gatewayHost ?? '?'} / {link.gatewaySid ?? '?'}
                </dd>
              </>
            )}
            <dt>Catalog server</dt>
            <dd>
              {link.targetServer ? (
                <Link to={`/server/${encodeURIComponent(link.targetServer)}`}>{link.targetServer}</Link>
              ) : (
                'Not present or not uniquely matched'
              )}
            </dd>
            <dt>Authentication</dt>
            <dd>Passwords are not extracted. Catalog links use observed metadata.</dd>
          </dl>
          {link.logins.length > 0 && (
            <ul>
              {link.logins.map((login, i) => (
                <li key={i}>
                  {login.localUser ? `${login.localUser} → ` : ''}
                  {login.usesSelf ? 'Caller identity' : (login.remoteUser ?? 'Remote user not visible')}
                </li>
              ))}
            </ul>
          )}
          {link.loginNodeIds.length > 0 && (
            <>
              <h3>Observed logins and users</h3>
              <ul>
                {link.loginNodeIds.map((id) => (
                  <li key={id}>
                    <Link to={`/object/${id}`}>{id}</Link>
                  </li>
                ))}
              </ul>
            </>
          )}
          {link.diagnostics.length > 0 && (
            <ul>
              {link.diagnostics.map((message, i) => (
                <li key={i}>{message}</li>
              ))}
            </ul>
          )}
          {link.evidence.length > 0 && (
            <details>
              <summary>Destination evidence</summary>
              <ul>
                {link.evidence.map((item, i) => (
                  <li key={i}>
                    {item.field}: <code>{item.value}</code> — {item.source}
                  </li>
                ))}
              </ul>
            </details>
          )}
        </>
      )}
      {node.principal && (
        <dl>
          <dt>Mapped login</dt>
          <dd>{node.principal.login ?? 'Not visible'}</dd>
          <dt>Default database</dt>
          <dd>{node.principal.defaultDatabase ?? 'Not recorded'}</dd>
          <dt>Default schema</dt>
          <dd>{node.principal.defaultSchema ?? 'Not recorded'}</dd>
        </dl>
      )}
    </section>
  )
}
