import { formatDialect, plsql, transactsql } from 'sql-formatter'
import { alignSqlColumns } from './alignSqlColumns'
import { sqlFormatConfig, type SqlFormatConfig } from './sqlStyleConfig'

/** Formatting is a display transform only; never write it back to the catalog. */
export function formatSql(code: string, config: SqlFormatConfig = sqlFormatConfig): { code: string; failed: boolean } {
  // Catalogs do not carry an engine field. Prefer PL/SQL for its distinctive
  // syntax, and try the other supported engine if parsing fails.
  const oracle = /\bCREATE\s+OR\s+REPLACE\b|\bVARCHAR2\b|\b(?:N?Q)'/i.test(code)
  for (const dialect of oracle ? [plsql, transactsql] : [transactsql, plsql]) {
    try {
      const formatted = formatDialect(code, {
        dialect,
        tabWidth: config.tabWidth,
        keywordCase: config.keywordCase,
        linesBetweenQueries: config.linesBetweenQueries,
      })
      return {
        code: config.alignColumns ? alignSqlColumns(formatted, config) : formatted,
        failed: false,
      }
    } catch {
      // Incomplete or unsupported definitions must still remain readable.
    }
  }
  return { code, failed: true }
}
