import { Fragment } from 'react'
import { parseMarkdown, type InlineNode, type MarkdownBlock } from '../lib/markdown'

/**
 * Renders the Markdown subset described in lib/markdown.ts as React elements.
 * Nothing goes through dangerouslySetInnerHTML - blocks become real nodes, so
 * a guide can never inject markup into the page.
 */
export default function Markdown({ source, className = 'markdown' }: { source: string; className?: string }) {
  const blocks = parseMarkdown(source)
  return (
    <div className={className}>
      {blocks.map((block, i) => (
        <Block key={i} block={block} />
      ))}
    </div>
  )
}

function Block({ block }: { block: MarkdownBlock }) {
  switch (block.kind) {
    case 'heading': {
      const content = <Inline nodes={block.content} />
      if (block.level === 1) return <h1>{content}</h1>
      if (block.level === 2) return <h2>{content}</h2>
      return <h3>{content}</h3>
    }
    case 'paragraph':
      return (
        <p>
          <Inline nodes={block.content} />
        </p>
      )
    case 'list': {
      const items = block.items.map((item, i) => (
        <li key={i}>
          <Inline nodes={item} />
        </li>
      ))
      return block.ordered ? <ol>{items}</ol> : <ul>{items}</ul>
    }
    case 'code':
      return (
        <pre className="markdown-code">
          <code>{block.text}</code>
        </pre>
      )
  }
}

function Inline({ nodes }: { nodes: InlineNode[] }) {
  return (
    <>
      {nodes.map((node, i) => {
        switch (node.kind) {
          case 'text':
            return <Fragment key={i}>{node.text}</Fragment>
          case 'code':
            return <code key={i}>{node.text}</code>
          case 'strong':
            return <strong key={i}>{node.text}</strong>
          case 'em':
            return <em key={i}>{node.text}</em>
          case 'link':
            return (
              <a key={i} href={node.href} target={/^https?:/i.test(node.href) ? '_blank' : undefined} rel="noreferrer">
                {node.text}
              </a>
            )
        }
      })}
    </>
  )
}
