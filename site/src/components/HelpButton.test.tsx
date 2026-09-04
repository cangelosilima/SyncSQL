import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it } from 'vitest'
import HelpButton from './HelpButton'
import { helpGuides, toGuide, type HelpTopic } from '../help'

const TOPICS: HelpTopic[] = ['overview', 'explorer', 'ai', 'lineage', 'history', 'object']

describe('HelpButton', () => {
  it('opens the page guide in a dialog and closes it again', async () => {
    const user = userEvent.setup()
    render(<HelpButton topic="explorer" />)

    const trigger = screen.getByRole('button', { name: 'Help: Explorer' })
    expect(trigger).toHaveAttribute('aria-expanded', 'false')
    expect(screen.queryByRole('dialog')).toBeNull()

    await user.click(trigger)
    const dialog = screen.getByRole('dialog', { name: 'Explorer' })
    expect(dialog).toBeInTheDocument()
    expect(trigger).toHaveAttribute('aria-expanded', 'true')

    await user.click(screen.getByRole('button', { name: 'Close help' }))
    expect(screen.queryByRole('dialog')).toBeNull()
    expect(trigger).toHaveFocus()
  })

  it('renders the guide markdown as real elements', async () => {
    const user = userEvent.setup()
    render(<HelpButton topic="explorer" />)
    await user.click(screen.getByRole('button', { name: 'Help: Explorer' }))

    expect(screen.getByRole('heading', { level: 2, name: 'DDL content search' })).toBeInTheDocument()
    expect(screen.getAllByRole('listitem').length).toBeGreaterThan(0)
  })

  it('closes on Escape', async () => {
    const user = userEvent.setup()
    render(<HelpButton topic="history" />)
    await user.click(screen.getByRole('button', { name: 'Help: History' }))
    expect(screen.getByRole('dialog')).toBeInTheDocument()

    await user.keyboard('{Escape}')
    expect(screen.queryByRole('dialog')).toBeNull()
  })
})

describe('help guides', () => {
  it.each(TOPICS)('%s has a title and a non-empty body', (topic) => {
    const guide = helpGuides[topic]
    expect(guide.title).not.toBe('')
    expect(guide.body.length).toBeGreaterThan(100)
    // The panel renders the title itself - the body must not repeat it as an H1.
    expect(guide.body.startsWith('# ')).toBe(false)
  })

  it('falls back to the given title when a document has no top-level heading', () => {
    expect(toGuide('just a body', 'Fallback')).toEqual({ title: 'Fallback', body: 'just a body' })
  })
})
