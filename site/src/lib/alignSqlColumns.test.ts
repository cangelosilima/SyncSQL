import { expect, it } from 'vitest'
import { alignSqlColumns } from './alignSqlColumns'
it('supports IF NOT EXISTS and alternative Oracle quote delimiters', () => {
  const sql = "CREATE TABLE IF NOT EXISTS t (a INT DEFAULT q'!text!', longer INT);"
  const formatted = alignSqlColumns(sql)
  expect(formatted).toContain("q'!text!'")
  expect(formatted).toMatch(/a +INT DEFAULT/)
  expect(alignSqlColumns('CREATE GLOBAL TEMPORARY TABLE t (a INT, longer INT)')).toMatch(/a +INT/)
})
it.each([
  "CREATE TABLE t (a INT DEFAULT q'[unterminated",
  'CREATE TABLE t (a INT',
  'CREATE TABLE (a INT)',
  'CREATE TABLE IF SOMETHING',
  'CREATE TABLE IF NOT MISSING',
  'CREATE TABLE t (a NOT NULL, b INT)',
])('preserves unsupported or incomplete syntax: %s', (sql) => {
  expect(alignSqlColumns(sql).replace(/\s/g, '')).toBe(sql.replace(/\s/g, ''))
})
