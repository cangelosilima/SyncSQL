import { describe, expect, it } from 'vitest'
import { diffLines } from './diff'

describe('diffLines', () => {
  it('marks every line equal for identical text and numbers both sides', () => {
    const result = diffLines('a\nb', 'a\nb')
    expect(result).toEqual([
      { type: 'equal', text: 'a', oldLineNo: 1, newLineNo: 1 },
      { type: 'equal', text: 'b', oldLineNo: 2, newLineNo: 2 },
    ])
  })

  it('reports an appended line as an insert with no old line number', () => {
    const result = diffLines('a', 'a\nb')
    expect(result).toEqual([
      { type: 'equal', text: 'a', oldLineNo: 1, newLineNo: 1 },
      { type: 'insert', text: 'b', oldLineNo: null, newLineNo: 2 },
    ])
  })

  it('reports a removed line as a delete with no new line number', () => {
    const result = diffLines('a\nb', 'b')
    expect(result).toEqual([
      { type: 'delete', text: 'a', oldLineNo: 1, newLineNo: null },
      { type: 'equal', text: 'b', oldLineNo: 2, newLineNo: 1 },
    ])
  })

  it('keeps the unchanged surroundings of a replaced line equal', () => {
    const result = diffLines('a\nb\nc', 'a\nx\nc')
    expect(result?.filter((l) => l.type === 'equal').map((l) => l.text)).toEqual(['a', 'c'])
    expect(result?.filter((l) => l.type === 'delete').map((l) => l.text)).toEqual(['b'])
    expect(result?.filter((l) => l.type === 'insert').map((l) => l.text)).toEqual(['x'])
  })

  it('advances old line numbers across deletes and new ones across inserts', () => {
    const result = diffLines('a\nb\nc', 'a\nx\nc')
    expect(result?.map((l) => [l.type, l.oldLineNo, l.newLineNo])).toEqual([
      ['equal', 1, 1],
      ['delete', 2, null],
      ['insert', null, 2],
      ['equal', 3, 3],
    ])
  })

  it('bails out with null rather than running the O(n*m) table on an oversized file', () => {
    const huge = Array.from({ length: 2001 }, (_, i) => `line ${i}`).join('\n')
    expect(diffLines(huge, 'a')).toBeNull()
    expect(diffLines('a', huge)).toBeNull()
  })

  it('still diffs a file right at the size limit', () => {
    const atLimit = Array.from({ length: 2000 }, (_, i) => `line ${i}`).join('\n')
    expect(diffLines(atLimit, atLimit)).not.toBeNull()
  })
})
