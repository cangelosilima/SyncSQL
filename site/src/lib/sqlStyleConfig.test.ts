import { describe, expect, it } from 'vitest'
import configuration from '../../../config/sql-style.json'
import { sqlFormatConfig, validateFormatConfig } from './sqlStyleConfig'
import { formatSql } from './formatSql'

describe('shared SQL style configuration', () => {
  it('uses the repository JSON as its formatting defaults', () => {
    expect(sqlFormatConfig).toEqual(configuration.format)
  })

  it('applies indentation, keyword case, query spacing and column spacing', () => {
    const result = formatSql('create table t (a int not null, longer varchar(20) null); select a from t;', {
      ...sqlFormatConfig,
      tabWidth: 4,
      columnSpacing: 3,
      keywordCase: 'upper',
      linesBetweenQueries: 3,
    })
    expect(result.failed).toBe(false)
    expect(result.code).toContain('CREATE TABLE t (\n    a        int           NOT NULL,')
    expect(result.code).toContain(';\n\n\n\nSELECT')
  })

  it('can turn off column alignment', () => {
    const result = formatSql('CREATE TABLE t (a INT, longer INT);', { ...sqlFormatConfig, alignColumns: false })
    expect(result.code).toContain('a INT,')
  })

  it('rejects invalid format values', () => {
    expect(() => validateFormatConfig({ ...sqlFormatConfig, tabWidth: 0 })).toThrow('tabWidth')
    expect(() => validateFormatConfig({ ...sqlFormatConfig, columnSpacing: 1.5 })).toThrow('columnSpacing')
    expect(() => validateFormatConfig({ ...sqlFormatConfig, linesBetweenQueries: -1 })).toThrow('linesBetweenQueries')
  })
})
