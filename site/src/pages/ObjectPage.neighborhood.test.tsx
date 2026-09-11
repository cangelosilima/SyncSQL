import { fireEvent, render, screen } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { expect, it, vi } from 'vitest'
import ObjectPage from './ObjectPage'
import { buildIndex } from '../lib/catalog'
import { makeCatalog, makeEdge, makeNode } from '../test/fixtures'

vi.mock('../lib/CatalogContext', () => ({
  useCatalog: () => ({
    index: buildIndex(
      makeCatalog({
        nodes: ['caller', 'focus', 'target']
          .map((id) => makeNode({ id }))
          .concat([
            makeNode({ id: 'incoming', type: 'LinkedServers' }),
            makeNode({ id: 'outgoing', type: 'DatabaseLinks' }),
          ]),
        edges: [
          makeEdge('caller', 'incoming'),
          makeEdge('incoming', 'focus'),
          makeEdge('focus', 'outgoing'),
          makeEdge('outgoing', 'target'),
        ],
        linkedServerReferences: [
          { linkedServer: 'incoming', from: 'caller', to: 'focus', schema: null, name: 'focus' },
          { linkedServer: 'outgoing', from: 'focus', to: 'target', schema: null, name: 'target' },
        ],
      }),
    ),
  }),
}))
vi.mock('../components/LineageGraph', () => ({
  default: ({ nodeIds }: { nodeIds: string[] }) => <div data-testid="graph">{nodeIds.join(',')}</div>,
}))

it('includes callers and remote targets beyond links in the object graph', () => {
  render(
    <MemoryRouter initialEntries={['/object/focus']}>
      <Routes>
        <Route path="/object/*" element={<ObjectPage />} />
      </Routes>
    </MemoryRouter>,
  )
  fireEvent.click(screen.getByRole('tab', { name: 'Graph' }))
  expect(screen.getByTestId('graph').textContent?.split(',').sort()).toEqual([
    'caller',
    'focus',
    'incoming',
    'outgoing',
    'target',
  ])
})
