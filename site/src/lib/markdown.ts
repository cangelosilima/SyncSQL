/**
 * A deliberately small Markdown subset parser, just enough for the in-app help
 * guides in `src/help/`. It exists instead of a dependency because the guides
 * are content we write ourselves: headings, paragraphs, lists, fenced code and
 * a handful of inline marks cover every one of them, and the site already
 * ships one heavyweight optional download (the AI model) too many.
 *
 * Everything here is pure - components/Markdown.tsx turns these blocks into
 * React elements, so nothing is ever injected as raw HTML.
 */

export type InlineNode =
  | { kind: 'text'; text: string }
  | { kind: 'code'; text: string }
  | { kind: 'strong'; text: string }
  | { kind: 'em'; text: string }
  | { kind: 'link'; text: string; href: string }

export type MarkdownBlock =
  | { kind: 'heading'; level: 1 | 2 | 3; content: InlineNode[] }
  | { kind: 'paragraph'; content: InlineNode[] }
  | { kind: 'list'; ordered: boolean; items: InlineNode[][] }
  | { kind: 'code'; language: string | null; text: string }

const HEADING = /^(#{1,3})\s+(.*)$/
const UNORDERED_ITEM = /^[-*]\s+(.*)$/
const ORDERED_ITEM = /^\d+[.)]\s+(.*)$/
const FENCE = /^```\s*([\w+-]*)\s*$/

/** Splits a Markdown document into the block types the help viewer renders. */
export function parseMarkdown(source: string): MarkdownBlock[] {
  const lines = source.replace(/\r\n?/g, '\n').split('\n')
  const blocks: MarkdownBlock[] = []
  let i = 0

  while (i < lines.length) {
    const line = lines[i]

    if (line.trim() === '') {
      i += 1
      continue
    }

    const fence = FENCE.exec(line.trim())
    if (fence) {
      const language = fence[1] || null
      const body: string[] = []
      i += 1
      while (i < lines.length && !/^```/.test(lines[i].trim())) {
        body.push(lines[i])
        i += 1
      }
      i += 1 // closing fence (or end of document)
      blocks.push({ kind: 'code', language, text: body.join('\n') })
      continue
    }

    const heading = HEADING.exec(line)
    if (heading) {
      blocks.push({
        kind: 'heading',
        level: heading[1].length as 1 | 2 | 3,
        content: parseInline(heading[2].trim()),
      })
      i += 1
      continue
    }

    const ordered = ORDERED_ITEM.test(line)
    if (ordered || UNORDERED_ITEM.test(line)) {
      const pattern = ordered ? ORDERED_ITEM : UNORDERED_ITEM
      const items: InlineNode[][] = []
      while (i < lines.length) {
        const match = pattern.exec(lines[i])
        if (match) {
          items.push(parseInline(match[1].trim()))
          i += 1
          continue
        }
        // An indented continuation line belongs to the item above it.
        if (items.length > 0 && /^\s+\S/.test(lines[i])) {
          const continued = lines[i].trim()
          const previous = items[items.length - 1]
          items[items.length - 1] = [...previous, { kind: 'text', text: ' ' }, ...parseInline(continued)]
          i += 1
          continue
        }
        break
      }
      blocks.push({ kind: 'list', ordered, items })
      continue
    }

    const paragraph: string[] = []
    while (
      i < lines.length &&
      lines[i].trim() !== '' &&
      !HEADING.test(lines[i]) &&
      !UNORDERED_ITEM.test(lines[i]) &&
      !ORDERED_ITEM.test(lines[i]) &&
      !FENCE.test(lines[i].trim())
    ) {
      paragraph.push(lines[i].trim())
      i += 1
    }
    blocks.push({ kind: 'paragraph', content: parseInline(paragraph.join(' ')) })
  }

  return blocks
}

const INLINE = /(`[^`]+`)|(\[[^\]]+\]\([^)\s]+\))|(\*\*[^*]+\*\*)|(\*[^*]+\*)/

/**
 * Splits one line into inline marks. Code spans win over everything else, so
 * `**not bold**` inside backticks stays literal - the guides lean on that when
 * documenting filter syntax.
 */
export function parseInline(text: string): InlineNode[] {
  const nodes: InlineNode[] = []
  let rest = text

  while (rest.length > 0) {
    const match = INLINE.exec(rest)
    if (!match || match.index === undefined) break

    if (match.index > 0) nodes.push({ kind: 'text', text: rest.slice(0, match.index) })
    const token = match[0]

    if (token.startsWith('`')) {
      nodes.push({ kind: 'code', text: token.slice(1, -1) })
    } else if (token.startsWith('[')) {
      const split = token.indexOf('](')
      const label = token.slice(1, split)
      const href = token.slice(split + 2, -1)
      if (isSafeHref(href)) nodes.push({ kind: 'link', text: label, href })
      else nodes.push({ kind: 'text', text: label })
    } else if (token.startsWith('**')) {
      nodes.push({ kind: 'strong', text: token.slice(2, -2) })
    } else {
      nodes.push({ kind: 'em', text: token.slice(1, -1) })
    }

    rest = rest.slice(match.index + token.length)
  }

  if (rest.length > 0) nodes.push({ kind: 'text', text: rest })
  return nodes
}

/**
 * Keeps `javascript:`-style hrefs out of the rendered guides. The guides are
 * ours, but they are still content rather than code, and a link that can't be
 * resolved to http(s), a mail address or an in-site route is not one we want
 * React to hand straight to the browser.
 */
export function isSafeHref(href: string): boolean {
  const value = href.trim()
  if (value === '') return false
  if (/^(https?:|mailto:)/i.test(value)) return true
  return /^[#/.]/.test(value)
}
