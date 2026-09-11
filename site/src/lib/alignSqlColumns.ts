import { sqlFormatConfig, type SqlFormatConfig } from './sqlStyleConfig'

interface Token {
  text: string
  start: number
  end: number
  kind: 'word' | 'identifier' | 'literal' | 'comment' | 'symbol'
}

// Only whitespace between tokens is rewritten. Quoted text and comments remain
// opaque, including escaped delimiters, Oracle q-quotes and nested comments.
function tokenize(code: string): Token[] {
  const tokens: Token[] = []
  const word = /[\p{L}\p{N}_$#@]+/uy
  for (let i = 0; i < code.length;) {
    if (/\s/.test(code[i])) { i++; continue }
    const start = i
    let kind: Token['kind'] = 'symbol'
    if (code.startsWith('--', i)) {
      kind = 'comment'
      while (i < code.length && code[i] !== '\n') i++
    } else if (code.startsWith('/*', i)) {
      kind = 'comment'
      i += 2
      let depth = 1
      while (i < code.length && depth) {
        if (code.startsWith('/*', i)) { depth++; i += 2 }
        else if (code.startsWith('*/', i)) { depth--; i += 2 }
        else i++
      }
    } else if (/^(?:n?q)'/i.test(code.slice(i, i + 3))) {
      kind = 'literal'
      const quote = code.indexOf("'", i)
      const open = code[quote + 1]
      const close = ({ '[': ']', '{': '}', '(': ')', '<': '>' } as Record<string, string>)[open] ?? open
      const end = code.indexOf(`${close}'`, quote + 2)
      i = end < 0 ? code.length : end + 2
    } else if (code[i] === "'" || code[i] === '"' || code[i] === '[') {
      kind = code[i] === "'" ? 'literal' : 'identifier'
      const close = code[i] === '[' ? ']' : code[i]
      i++
      while (i < code.length) {
        if (code[i++] !== close) continue
        if (code[i] === close) i++
        else break
      }
    } else {
      word.lastIndex = i
      const match = word.exec(code)
      if (match) { kind = 'word'; i = word.lastIndex }
      else i++
    }
    tokens.push({ text: code.slice(start, i), start, end: i, kind })
  }
  return tokens
}

const isWord = (token: Token | undefined, pattern: RegExp) => token?.kind === 'word' && pattern.test(token.text)
const isIdentifier = (token: Token | undefined) => token?.kind === 'word' || token?.kind === 'identifier'
const constraints = /^(CONSTRAINT|PRIMARY|FOREIGN|UNIQUE|CHECK|INDEX|KEY|PERIOD|SUPPLEMENTAL|LIKE)$/i
const modifiers = /^(NOT|NULL|DEFAULT|IDENTITY|GENERATED|COLLATE|CONSTRAINT|PRIMARY|REFERENCES|CHECK|UNIQUE|SPARSE|ROWGUIDCOL|FILESTREAM|MASKED|ENCRYPTED|HIDDEN|VISIBLE|INVISIBLE|ENABLE|DISABLE|AS)$/i

function compact(tokens: Token[]): string {
  return tokens.map((token, i) => (i && token.start > tokens[i - 1].end ? ' ' : '') + token.text).join('')
}

/** Align CREATE TABLE column declarations; leave queries and table constraints alone. */
export function alignSqlColumns(code: string, config: Pick<SqlFormatConfig, 'tabWidth' | 'columnSpacing'> = sqlFormatConfig): string {
  // Honor the formatter's opt-out comments as well.
  if (code.includes('sql-formatter-disable')) return code
  const tokens = tokenize(code)
  const edits: { start: number; end: number; text: string }[] = []
  for (let i = 0; i < tokens.length; i++) {
    if (!isWord(tokens[i], /^CREATE$/i)) continue
    let cursor = i + 1
    while (isWord(tokens[cursor], /^(GLOBAL|LOCAL|TEMPORARY|TEMP)$/i)) cursor++
    if (!isWord(tokens[cursor++], /^TABLE$/i)) continue
    if (isWord(tokens[cursor], /^IF$/i) && isWord(tokens[cursor + 1], /^NOT$/i) && isWord(tokens[cursor + 2], /^EXISTS$/i)) cursor += 3
    if (!isIdentifier(tokens[cursor++])) continue
    while (tokens[cursor]?.text === '.' && isIdentifier(tokens[cursor + 1])) cursor += 2
    if (tokens[cursor]?.text !== '(') continue
    const open = cursor
    const rows: { start: number; end: number; tokens: Token[]; last: boolean }[] = []
    let depth = 1
    let rowStart = tokens[open].end
    let tokenStart = open + 1
    for (cursor = open + 1; cursor < tokens.length; cursor++) {
      const token = tokens[cursor]
      if (token.kind !== 'symbol') continue
      if (token.text === '(') depth++
      if (token.text === ')') depth--
      if (depth === 0 || (depth === 1 && token.text === ',')) {
        rows.push({ start: rowStart, end: token.start, tokens: tokens.slice(tokenStart, cursor), last: depth === 0 })
        rowStart = token.end
        tokenStart = cursor + 1
      }
      if (depth === 0) break
    }
    if (depth !== 0) continue
    const columns = rows.flatMap(row => {
      const parts = row.tokens
      if (!isIdentifier(parts[0]) || isWord(parts[0], constraints) || !isIdentifier(parts[1])) return []
      // Keep commented, multiline-literal and computed declarations untouched.
      if (parts.some(token => token.kind === 'comment' || /[\r\n\t]/.test(token.text)) || isWord(parts[1], /^AS$/i)) return []
      let modifier = parts.length
      let level = 0
      for (let j = 1; j < parts.length; j++) {
        if (level === 0 && isWord(parts[j], modifiers)) { modifier = j; break }
        if (parts[j].text === '(') level++
        if (parts[j].text === ')') level--
      }
      if (modifier === 1) return []
      return [{ row, name: parts[0].text, type: compact(parts.slice(1, modifier)), modifier: compact(parts.slice(modifier)) }]
    })
    const nameWidth = Math.max(0, ...columns.map(column => column.name.length))
    const typeWidth = Math.max(0, ...columns.map(column => column.type.length))
    const lineStart = code.lastIndexOf('\n', tokens[i].start) + 1
    const indent = code.slice(lineStart, tokens[i].start).match(/^\s*/)?.[0] ?? ''
    for (const column of columns) {
      const gap = ' '.repeat(config.columnSpacing)
      edits.push({
        start: column.row.start,
        end: column.row.end,
        text: `\n${indent}${' '.repeat(config.tabWidth)}${column.name.padEnd(nameWidth)}${gap}${column.modifier ? column.type.padEnd(typeWidth) + gap + column.modifier : column.type}${column.row.last ? '\n' + indent : ''}`,
      })
    }
    i = cursor
  }
  for (const edit of edits.reverse()) code = code.slice(0, edit.start) + edit.text + code.slice(edit.end)
  return code
}
