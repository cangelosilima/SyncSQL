export type DiffOpType = 'equal' | 'insert' | 'delete'

export interface DiffLine {
  type: DiffOpType
  text: string
  oldLineNo: number | null
  newLineNo: number | null
}

const MAX_LINES = 2000

/**
 * Classic O(n*m) LCS-based line diff (dependency-free, matching this app's
 * other from-scratch visuals - see TrendChart/graphExport). DDL files are
 * small enough that this is fine; returns null rather than hanging the
 * browser on the rare oversized file.
 */
export function diffLines(oldText: string, newText: string): DiffLine[] | null {
  const a = oldText.split('\n')
  const b = newText.split('\n')
  if (a.length > MAX_LINES || b.length > MAX_LINES) return null

  const n = a.length
  const m = b.length
  const dp: Uint32Array[] = new Array(n + 1)
  for (let i = 0; i <= n; i++) dp[i] = new Uint32Array(m + 1)

  for (let i = n - 1; i >= 0; i--) {
    for (let j = m - 1; j >= 0; j--) {
      dp[i][j] = a[i] === b[j] ? dp[i + 1][j + 1] + 1 : Math.max(dp[i + 1][j], dp[i][j + 1])
    }
  }

  const result: DiffLine[] = []
  let i = 0
  let j = 0
  let oldNo = 1
  let newNo = 1
  while (i < n && j < m) {
    if (a[i] === b[j]) {
      result.push({ type: 'equal', text: a[i], oldLineNo: oldNo++, newLineNo: newNo++ })
      i++
      j++
    } else if (dp[i + 1][j] >= dp[i][j + 1]) {
      result.push({ type: 'delete', text: a[i], oldLineNo: oldNo++, newLineNo: null })
      i++
    } else {
      result.push({ type: 'insert', text: b[j], oldLineNo: null, newLineNo: newNo++ })
      j++
    }
  }
  while (i < n) {
    result.push({ type: 'delete', text: a[i], oldLineNo: oldNo++, newLineNo: null })
    i++
  }
  while (j < m) {
    result.push({ type: 'insert', text: b[j], oldLineNo: null, newLineNo: newNo++ })
    j++
  }

  return result
}
