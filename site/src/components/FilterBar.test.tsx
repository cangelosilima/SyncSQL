import { useState } from 'react'
import { fireEvent, render, renderHook, screen } from '@testing-library/react'
import { expect, it } from 'vitest'
import FilterBar, { useFilteredNodes } from './FilterBar'
import { makeNode } from '../test/fixtures'
import type { FilterToken } from '../lib/filters'
const nodes = [makeNode({ id: 'a' }), makeNode({ id: 'b', type: 'Views' })]
function Editor() {
  const [tokens, setTokens] = useState<FilterToken[]>([])
  return <FilterBar nodes={nodes} tokens={tokens} onChange={setTokens} />
}
const input = () => screen.getByRole('textbox')
const key = (key: string) => fireEvent.keyDown(input(), { key })
const type = (value: string) => fireEvent.change(input(), { target: { value } })
const pick = (name: string) => fireEvent.mouseDown(screen.getByRole('option', { name }))
it('selects multiple values, toggles them, edits drafts and applies a multi-value filter', () => {
  render(<Editor />)
  fireEvent.focus(input())
  pick('Type')
  pick('is in')
  pick('Tables')
  pick('Views')
  pick('✓ Tables')
  expect(input()).toHaveAttribute('placeholder', 'Views - Enter to apply')
  key('Backspace')
  expect(input()).toHaveAttribute('placeholder', 'Type is in...')
  type('Custom')
  fireEvent.blur(input())
  key('Enter')
  type('Custom')
  key('Enter')
  expect(input()).toHaveAttribute('placeholder', 'Custom - Enter to apply')
  key('Enter')
  expect(screen.getByRole('button', { name: 'Remove filter Type is in Custom' })).toBeVisible()
  key('Backspace')
  expect(screen.queryByRole('button')).not.toBeInTheDocument()
  key('Backspace')
  key('Enter')
})
it('backs out through value and operator stages, cancels drafts and supports keyboard value selection', () => {
  render(<Editor />)
  fireEvent.focus(input())
  pick('Type')
  pick('is not in')
  key('Backspace')
  expect(input()).toHaveAttribute('placeholder', 'Type...')
  key('Backspace')
  expect(input()).toHaveAttribute('placeholder', 'Filter by attribute, or type to search...')
  pick('Type')
  pick('is')
  key('Enter')
  expect(screen.getByRole('button', { name: 'Remove filter Type is Tables' })).toBeVisible()
  type('draft')
  key('Escape')
  expect(input()).toHaveValue('')
  fireEvent.focus(input())
  pick('Name')
  pick('contains')
  key('Enter')
  expect(screen.getAllByRole('button')).toHaveLength(1)
  type('name')
  key('Enter')
  expect(screen.getByRole('button', { name: 'Remove filter Name contains name' })).toBeVisible()
  fireEvent.click(screen.getByRole('button', { name: 'Remove filter Name contains name' }))
  type('no suggestions')
  key('ArrowDown')
  key('ArrowUp')
  key('Enter')
  expect(screen.getByRole('button', { name: 'Remove filter no suggestions' })).toBeVisible()
})
it('memoizes filtered node results', () => {
  const { result } = renderHook(() =>
    useFilteredNodes(nodes, [{ id: 'a', attribute: 'type', operator: 'is', values: ['Views'] }]),
  )
  expect(result.current).toEqual([nodes[1]])
})
