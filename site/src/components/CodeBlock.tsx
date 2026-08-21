import { useMemo } from 'react'
import hljs from 'highlight.js/lib/core'
import sql from 'highlight.js/lib/languages/sql'
import './midnight-hljs.css'

hljs.registerLanguage('sql', sql)

interface CodeBlockProps {
  code: string
}

export default function CodeBlock({ code }: CodeBlockProps) {
  const html = useMemo(() => {
    if (!code) return ''
    return hljs.highlight(code, { language: 'sql' }).value
  }, [code])

  if (!code) {
    return <div className="code-block code-block--empty">No definition captured.</div>
  }

  return (
    <pre className="code-block">
      <code dangerouslySetInnerHTML={{ __html: html }} />
    </pre>
  )
}
