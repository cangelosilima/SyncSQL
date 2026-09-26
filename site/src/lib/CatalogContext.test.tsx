import { act, render, screen } from '@testing-library/react'
import { expect, it, vi } from 'vitest'
import { CatalogProvider, useCatalog } from './CatalogContext'
import { buildIndex, loadCatalog } from './catalog'
import { makeCatalog } from '../test/fixtures'
vi.mock('./catalog', async (original) => ({ ...(await original<typeof import('./catalog')>()), loadCatalog: vi.fn() }))
function Status() {
  const { loading, error, index } = useCatalog()
  return <output>{loading ? 'Loading' : (error ?? index?.catalog.generatedAt)}</output>
}
it('provides a loading default and then the loaded catalog', async () => {
  const index = buildIndex(makeCatalog())
  vi.mocked(loadCatalog).mockResolvedValueOnce(index)
  const { rerender } = render(<Status />)
  expect(screen.getByText('Loading')).toBeVisible()
  rerender(
    <CatalogProvider>
      <Status />
    </CatalogProvider>,
  )
  expect(await screen.findByText(index.catalog.generatedAt)).toBeVisible()
})
it.each([new Error('Offline'), 'Offline'])('reports load failures (%s)', async (error) => {
  vi.mocked(loadCatalog).mockRejectedValueOnce(error)
  render(
    <CatalogProvider>
      <Status />
    </CatalogProvider>,
  )
  expect(await screen.findByText('Offline')).toBeVisible()
})
it.each([false, true])('ignores load completion after unmount (failure=%s)', async (failure) => {
  let finish!: () => void
  vi.mocked(loadCatalog).mockImplementationOnce(
    () =>
      new Promise((resolve, reject) => {
        finish = () => (failure ? reject(new Error('Offline')) : resolve(buildIndex(makeCatalog())))
      }),
  )
  const { unmount } = render(
    <CatalogProvider>
      <Status />
    </CatalogProvider>,
  )
  unmount()
  await act(async () => finish())
  expect(screen.queryByText('Offline')).not.toBeInTheDocument()
})
