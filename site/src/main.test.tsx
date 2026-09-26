import { expect, it, vi } from 'vitest'
const { render, createRoot } = vi.hoisted(() => {
  const render = vi.fn()
  return { render, createRoot: vi.fn(() => ({ render })) }
})
vi.mock('react-dom/client', () => ({ createRoot }))
vi.mock('./App', () => ({ default: () => null }))
it('mounts the application into the root element', async () => {
  const root = document.createElement('div')
  root.id = 'root'
  document.body.append(root)
  await import('./main')
  expect(createRoot).toHaveBeenCalledWith(root)
  expect(render).toHaveBeenCalledWith(
    expect.objectContaining({ props: expect.objectContaining({ children: expect.anything() }) }),
  )
  root.remove()
})
