import configuration from '../../../config/sql-style.json'
import type { KeywordCase } from 'sql-formatter'

export interface SqlFormatConfig {
  tabWidth: number
  keywordCase: KeywordCase
  linesBetweenQueries: number
  alignColumns: boolean
  columnSpacing: number
}

export function validateFormatConfig(value: SqlFormatConfig): SqlFormatConfig {
  for (const [key, min, max] of [['tabWidth', 1, 8], ['linesBetweenQueries', 0, 10], ['columnSpacing', 1, 8]] as const) {
    if (!Number.isInteger(value[key]) || value[key] < min || value[key] > max) throw new Error(`Invalid SQL format.${key}: expected an integer from ${min} to ${max}.`)
  }
  if (!['preserve', 'upper', 'lower'].includes(value.keywordCase)) throw new Error('Invalid SQL format.keywordCase.')
  if (typeof value.alignColumns !== 'boolean') throw new Error('Invalid SQL format.alignColumns: expected a boolean.')
  return value
}

if (configuration.version !== 1) throw new Error('Unsupported SQL style configuration version.')
export const sqlFormatConfig = validateFormatConfig(configuration.format as SqlFormatConfig)
