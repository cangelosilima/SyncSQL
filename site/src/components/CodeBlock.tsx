import { useMemo } from 'react'
import hljs from 'highlight.js/lib/core'
import sql from 'highlight.js/lib/languages/sql'
import { formatSql } from '../lib/formatSql'
import './midnight-hljs.css'

hljs.registerLanguage('sql', sql)

interface CodeBlockProps {
  code: string
  formatted?: boolean
}

export default function CodeBlock({ code, formatted = false }: CodeBlockProps) {
  const display = useMemo(() => code && formatted ? formatSql(code) : { code, failed: false }, [code, formatted])
  const html = useMemo(() => {
    if (!display.code) return ''
    return hljs.highlight(display.code, { language: 'sql' }).value
  }, [display.code])

  if (!code) {
    return <div className="code-block code-block--empty">No definition captured.</div>
  }

  return (
    <>
      {display.failed && <p className="muted" role="status">This definition could not be formatted. Showing the original SQL with line wrapping.</p>}
      <pre className={`code-block${formatted ? ' code-block--formatted' : ''}`} tabIndex={0} role="region" aria-label="SQL definition">
        <code dangerouslySetInnerHTML={{ __html: html }} />
      </pre>
    </>
  )
}
