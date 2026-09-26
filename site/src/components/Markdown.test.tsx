import { render, screen } from '@testing-library/react'
import { expect, it } from 'vitest'
import Markdown from './Markdown'
it('renders supported blocks and safe inline formatting with appropriate link targets', () => {
  const { container } = render(
    <Markdown
      className="guide"
      source={
        '# One\n\n## Two\n\n### Three\n\nText **bold** *emphasis* `code` [web](https://example.com) [local](#one)\n\n1. ordered\n2. second\n\n- bullet\n\n```sql\nSELECT 1\n```'
      }
    />,
  )
  expect(screen.getAllByRole('heading')).toHaveLength(3)
  expect(container.querySelector('strong')).toHaveTextContent('bold')
  expect(container.querySelector('em')).toHaveTextContent('emphasis')
  expect(container.querySelector('ol')).toHaveTextContent('ordered')
  expect(container.querySelector('ul')).toHaveTextContent('bullet')
  expect(container.querySelector('pre')).toHaveTextContent('SELECT 1')
  expect(screen.getByRole('link', { name: 'web' })).toHaveAttribute('target', '_blank')
  expect(screen.getByRole('link', { name: 'local' })).not.toHaveAttribute('target')
})
