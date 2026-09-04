import { describe, expect, it } from 'vitest'
import { isSafeHref, parseInline, parseMarkdown } from './markdown'

describe('parseMarkdown', () => {
  it('reads headings at their level', () => {
    expect(parseMarkdown('# One\n\n## Two\n\n### Three')).toEqual([
      { kind: 'heading', level: 1, content: [{ kind: 'text', text: 'One' }] },
      { kind: 'heading', level: 2, content: [{ kind: 'text', text: 'Two' }] },
      { kind: 'heading', level: 3, content: [{ kind: 'text', text: 'Three' }] },
    ])
  })

  it('joins wrapped lines into one paragraph and splits on a blank line', () => {
    const blocks = parseMarkdown('first line\nstill first\n\nsecond')
    expect(blocks).toHaveLength(2)
    expect(blocks[0]).toEqual({ kind: 'paragraph', content: [{ kind: 'text', text: 'first line still first' }] })
    expect(blocks[1]).toEqual({ kind: 'paragraph', content: [{ kind: 'text', text: 'second' }] })
  })

  it('reads unordered and ordered lists, and folds indented continuation lines into the item', () => {
    const blocks = parseMarkdown('- alpha\n  wrapped\n- beta\n\n1. one\n2. two')
    expect(blocks[0]).toEqual({
      kind: 'list',
      ordered: false,
      items: [
        [{ kind: 'text', text: 'alpha' }, { kind: 'text', text: ' ' }, { kind: 'text', text: 'wrapped' }],
        [{ kind: 'text', text: 'beta' }],
      ],
    })
    expect(blocks[1]).toEqual({
      kind: 'list',
      ordered: true,
      items: [[{ kind: 'text', text: 'one' }], [{ kind: 'text', text: 'two' }]],
    })
  })

  it('keeps fenced code verbatim, including markup that would otherwise be parsed', () => {
    const blocks = parseMarkdown('```sql\nSELECT * FROM dbo.Orders\n-- not a list\n```')
    expect(blocks).toEqual([{ kind: 'code', language: 'sql', text: 'SELECT * FROM dbo.Orders\n-- not a list' }])
  })

  it('closes an unterminated fence at the end of the document', () => {
    expect(parseMarkdown('```\nstill code')).toEqual([{ kind: 'code', language: null, text: 'still code' }])
  })

  it('handles CRLF documents', () => {
    expect(parseMarkdown('# Title\r\n\r\nbody')).toEqual([
      { kind: 'heading', level: 1, content: [{ kind: 'text', text: 'Title' }] },
      { kind: 'paragraph', content: [{ kind: 'text', text: 'body' }] },
    ])
  })
})

describe('parseInline', () => {
  it('reads code, bold, italic and links', () => {
    expect(parseInline('a `code` b **bold** c *em* d [link](https://example.com) e')).toEqual([
      { kind: 'text', text: 'a ' },
      { kind: 'code', text: 'code' },
      { kind: 'text', text: ' b ' },
      { kind: 'strong', text: 'bold' },
      { kind: 'text', text: ' c ' },
      { kind: 'em', text: 'em' },
      { kind: 'text', text: ' d ' },
      { kind: 'link', text: 'link', href: 'https://example.com' },
      { kind: 'text', text: ' e' },
    ])
  })

  it('leaves markup inside a code span literal', () => {
    expect(parseInline('`**not bold**`')).toEqual([{ kind: 'code', text: '**not bold**' }])
  })

  it('renders an unsafe link as plain text rather than an anchor', () => {
    expect(parseInline('[click](javascript:alert)')).toEqual([{ kind: 'text', text: 'click' }])
    expect(parseInline('[click](data:text/html,x)')).toEqual([{ kind: 'text', text: 'click' }])
  })
})

describe('isSafeHref', () => {
  it.each(['https://example.com', 'http://example.com', 'mailto:a@b.c', '/explorer', '#anchor', './guide.md'])(
    'accepts %s',
    (href) => expect(isSafeHref(href)).toBe(true),
  )

  it.each(['javascript:alert(1)', 'JavaScript:alert(1)', 'data:text/html,<script>', '', '   '])(
    'rejects %s',
    (href) => expect(isSafeHref(href)).toBe(false),
  )
})
