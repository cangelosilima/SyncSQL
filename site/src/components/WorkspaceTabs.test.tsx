import { fireEvent, render, screen } from '@testing-library/react'
import { expect, it, vi } from 'vitest'
import WorkspaceTabs, { WorkspacePanel } from './WorkspaceTabs'
it('supports tab navigation with arrow, Home and End keys and ignores other keys', () => {
  const onChange = vi.fn()
  render(
    <>
      <WorkspaceTabs label="Views" items={['One', 'Two', 'Three']} value="One" onChange={onChange} />
      <WorkspacePanel name="One" active="One" panelPrefix="custom">
        Content
      </WorkspacePanel>
    </>,
  )
  for (const [key, expected] of [
    ['End', 'Three'],
    ['Home', 'One'],
    ['ArrowLeft', 'Three'],
    ['ArrowRight', 'Two'],
  ]) {
    fireEvent.keyDown(screen.getByRole('tab', { name: 'One' }), { key })
    expect(onChange).toHaveBeenLastCalledWith(expected)
    expect(screen.getByRole('tab', { name: expected })).toHaveFocus()
  }
  onChange.mockClear()
  fireEvent.keyDown(screen.getByRole('tab', { name: 'One' }), { key: 'Escape' })
  expect(onChange).not.toHaveBeenCalled()
  fireEvent.click(screen.getByRole('tab', { name: 'Two' }))
  expect(onChange).toHaveBeenCalledWith('Two')
  expect(screen.getByRole('tabpanel')).toHaveAttribute('id', 'custom-One')
})
