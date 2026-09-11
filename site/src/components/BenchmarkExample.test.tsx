import { fireEvent, render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { expect, it } from 'vitest'
import BenchmarkExample from './BenchmarkExample'
import snapshot from '../../public/data/catalog.json'
import type { Catalog } from '../types'
import { makeCatalog } from '../test/fixtures'
import { buildIndex } from '../lib/catalog'
import { findObjectsForGrantee } from '../lib/grants'

const catalog = snapshot as unknown as Catalog

it('offers all scenario paths, preserves repeated cycle endpoints and links every object', () => {
  const { container } = render(
    <MemoryRouter>
      <BenchmarkExample catalog={catalog} />
    </MemoryRouter>,
  )
  expect(screen.getByRole('heading', { name: 'Helios, Atlas & Meridian' })).toBeVisible()
  expect(container.querySelectorAll('details')).toHaveLength(catalog.example!.paths.length)
  expect(container.querySelectorAll('ol li')).toHaveLength(
    catalog.example!.paths.reduce((sum, path) => sum + path.nodes.length, 0),
  )
  for (const link of container.querySelectorAll<HTMLAnchorElement>('ol a')) {
    expect(buildIndex(catalog).byId.has(link.getAttribute('href')!.slice('/object/'.length))).toBe(true)
  }
  expect(screen.getByText(/Linux SQL Server cannot execute/)).toBeVisible()
})

it('lets visitors investigate each of the 48 workload users using recorded grants', () => {
  render(
    <MemoryRouter>
      <BenchmarkExample catalog={catalog} />
    </MemoryRouter>,
  )
  expect(screen.getAllByRole('option')).toHaveLength(49)
  for (const user of catalog.example!.users) {
    expect(findObjectsForGrantee(catalog.nodes, user.name, true).length).toBeGreaterThan(0)
  }
  const user = catalog.example!.users[0].name
  fireEvent.change(screen.getByLabelText('Investigate a user'), { target: { value: user } })
  expect(screen.getByRole('link', { name: 'View recorded access' })).toHaveAttribute(
    'href',
    `/lineage?tab=access&grantee=${user}&exact=1`,
  )
})

it('does not label a regular catalog as a benchmark', () => {
  const { container } = render(<BenchmarkExample catalog={makeCatalog()} />)
  expect(container).toBeEmptyDOMElement()
})
