import { render, screen } from '@testing-library/react'
import TypeBadge from './TypeBadge'
import { colorForType } from '../lib/typeColors'

describe('TypeBadge', () => {
  it('renders the type name', () => {
    render(<TypeBadge type="Tables" />)
    expect(screen.getByText('Tables')).toBeInTheDocument()
  })

  it('exposes the type colour as the --badge-color custom property', () => {
    render(<TypeBadge type="Tables" />)
    expect(screen.getByText('Tables').style.getPropertyValue('--badge-color')).toBe(colorForType('Tables'))
  })

  it('falls back to the neutral colour for an unknown type', () => {
    render(<TypeBadge type="Sequences" />)
    expect(screen.getByText('Sequences').style.getPropertyValue('--badge-color')).toBe(colorForType('Unknown'))
  })
})
