import { render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { expect, it } from 'vitest'
import { makeNode } from '../test/fixtures'
import LinkConnectionDetails from './LinkConnectionDetails'
it('renders absent and populated principal context', () => {
  const node = makeNode({ id: 'user' })
  const { container, rerender } = render(<LinkConnectionDetails node={node} />)
  expect(container).toBeEmptyDOMElement()
  node.principal = { login: null, defaultDatabase: null, defaultSchema: null }
  rerender(<LinkConnectionDetails node={node} />)
  expect(screen.getByRole('heading', { name: 'Principal context' })).toBeVisible()
  expect(screen.getByText('Not visible')).toBeVisible()
  node.principal = { login: 'Login', defaultDatabase: 'Db', defaultSchema: 'dbo' }
  rerender(<LinkConnectionDetails node={node} />)
  expect(screen.getByText('Login')).toBeVisible()
  expect(screen.getByText('Db')).toBeVisible()
  expect(screen.getByText('dbo')).toBeVisible()
})
it.each([true, false])('renders partial gateways and login mappings (host=%s)', (host) => {
  const node = makeNode({ id: 'link' })
  node.link = {
    connectIdentifier: null,
    dataSource: null,
    targetEngine: null,
    database: null,
    defaultSchema: null,
    gatewayHost: host ? 'gateway' : null,
    gatewaySid: host ? null : 'sid',
    targetServer: null,
    loginNodeIds: [],
    logins: [
      { localUser: 'local', remoteUser: null, usesSelf: true },
      { localUser: null, remoteUser: null, usesSelf: false },
    ],
    passwordStatus: 'not-extracted',
    evidence: [],
    diagnostics: ['Destination unknown'],
  }
  render(
    <MemoryRouter>
      <LinkConnectionDetails node={node} />
    </MemoryRouter>,
  )
  expect(screen.getByText('Endpoint not resolved')).toBeVisible()
  expect(screen.getByText('local → Caller identity')).toBeVisible()
  expect(screen.getByText('Remote user not visible')).toBeVisible()
  expect(screen.getByText('Destination unknown')).toBeVisible()
})

it('keeps the observed destination and identity visible without a traversable target', () => {
  const node = {
    ...makeNode({ id: 'DL_ORDER' }),
    link: {
      connectIdentifier: 'GATEWAY_DEVELOPMENT',
      dataSource: 'sql-dev,1433',
      targetEngine: 'mssql',
      database: 'Orders',
      defaultSchema: null,
      gatewayHost: 'tg-dev',
      gatewaySid: 'orders',
      targetServer: null,
      loginNodeIds: [],
      logins: [{ remoteUser: 'entitlements', localUser: null, usesSelf: false }],
      passwordStatus: 'not-extracted',
      evidence: [{ field: 'dataSource', value: 'sql-dev,1433', source: 'gateway:initorders.ora' }],
      diagnostics: [],
    },
  }
  render(
    <MemoryRouter>
      <LinkConnectionDetails node={node} />
    </MemoryRouter>,
  )
  expect(screen.getByText('entitlements')).toBeInTheDocument()
  expect(screen.getByText('Not present or not uniquely matched')).toBeInTheDocument()
  expect(screen.getByText('sql-dev,1433 (mssql)')).toBeInTheDocument()
})

it('links to independently extracted principals', () => {
  const id = 'SQL/_ServerLevel/Logins/entitlements'
  const node = {
    ...makeNode({ id: 'DL_ORDER' }),
    link: {
      connectIdentifier: null,
      dataSource: 'sql-dev',
      targetEngine: 'mssql',
      database: 'Orders',
      defaultSchema: 'sales',
      gatewayHost: null,
      gatewaySid: null,
      targetServer: 'SQL',
      loginNodeIds: [id],
      logins: [],
      passwordStatus: 'not-extracted',
      evidence: [],
      diagnostics: [],
    },
  }
  render(
    <MemoryRouter>
      <LinkConnectionDetails node={node} />
    </MemoryRouter>,
  )
  expect(screen.getByRole('link', { name: id })).toHaveAttribute('href', `/object/${id}`)
  expect(screen.getByRole('link', { name: 'SQL' })).toHaveAttribute('href', '/server/SQL')
})
