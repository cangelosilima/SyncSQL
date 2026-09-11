import { describe, expect, it } from 'vitest'
import { formatSql } from './formatSql'
import { alignSqlColumns } from './alignSqlColumns'

describe('table definition alignment', () => {
  it('aligns Oracle column names, types and modifiers without padding empty modifiers', () => {
    const result = formatSql(
      'CREATE TABLE "COMPLIANCE"."ITEMS" ("ID" NUMBER NOT NULL ENABLE, "AMOUNT" NUMBER, "LABEL" VARCHAR2(40 CHAR) DEFAULT \'unchanged\', PRIMARY KEY ("ID") USING INDEX ENABLE);',
    )
    expect(result.failed).toBe(false)
    expect(result.code).toContain('  "ID"     NUMBER            NOT NULL ENABLE,')
    expect(result.code).toContain('  "AMOUNT" NUMBER,')
    expect(result.code).toContain('  "LABEL"  VARCHAR2(40 CHAR) DEFAULT \'unchanged\',')
    expect(result.code).toContain('  PRIMARY KEY ("ID") USING INDEX ENABLE')
  })

  it('aligns parameterized and qualified SQL Server types while retaining computed columns', () => {
    const result = formatSql(
      'CREATE TABLE dbo.Items ([Id] int IDENTITY(1,1) NOT NULL, [Name] nvarchar(100) NULL, [Amount] decimal(18,2) DEFAULT (0) NOT NULL, [Code] [dbo].[CodeType] NULL, [Double] AS ([Amount]*2) PERSISTED, CONSTRAINT PK_Items PRIMARY KEY ([Id]));',
    )
    const rows = result.code.split('\n')
    const id = rows.find((row) => row.includes('[Id] int') || row.includes('IDENTITY'))!
    const name = rows.find((row) => row.includes('[Name]'))!
    const amount = rows.find((row) => row.includes('decimal'))!
    const custom = rows.find((row) => row.includes('[Code]'))!
    expect([name.indexOf('nvarchar'), amount.indexOf('decimal'), custom.indexOf('[dbo]')]).toEqual([
      id.indexOf('int'),
      id.indexOf('int'),
      id.indexOf('int'),
    ])
    expect([name.indexOf('NULL'), amount.indexOf('DEFAULT'), custom.indexOf('NULL')]).toEqual([
      id.indexOf('IDENTITY'),
      id.indexOf('IDENTITY'),
      id.indexOf('IDENTITY'),
    ])
    expect(result.code).toContain('decimal(18, 2)')
    expect(result.code).toContain('[Double] AS ([Amount] * 2) PERSISTED')
    expect(result.failed).toBe(false)
  })

  it('keeps multiword types together and preserves literal whitespace and punctuation', () => {
    const result = formatSql(
      "CREATE TABLE t (created TIMESTAMP(6) WITH LOCAL TIME ZONE DEFAULT CURRENT_TIMESTAMP, length DOUBLE PRECISION NOT NULL, label VARCHAR2(40 CHAR) DEFAULT q'[it's  unchanged,);]', note VARCHAR2(50) DEFAULT 'a  b''c');",
    )
    expect(result.code).toContain('TIMESTAMP(6) WITH LOCAL TIME ZONE DEFAULT CURRENT_TIMESTAMP')
    expect(result.code).toContain('DOUBLE PRECISION')
    expect(result.code).toContain("q'[it's  unchanged,);]'")
    expect(result.code).toContain("'a  b''c'")
    expect(result.failed).toBe(false)
  })

  it('aligns each table independently and ignores CREATE TABLE text in strings and comments', () => {
    const result = formatSql(
      "SELECT 'CREATE TABLE fake (a INT, longer VARCHAR(20))'; -- CREATE TABLE fake (a INT, longer INT)\nCREATE TABLE first_table (a INT NOT NULL, longer VARCHAR(20) NULL); CREATE TABLE second_table (b INT NULL, c INT NOT NULL);",
    )
    expect(result.code).toContain("'CREATE TABLE fake (a INT, longer VARCHAR(20))'")
    expect(result.code).toContain('-- CREATE TABLE fake (a INT, longer INT)')
    expect(result.code).toContain('  a      INT         NOT NULL,')
    expect(result.code).toContain('  b INT NULL,')
    expect(result.code).toContain('  c INT NOT NULL')
  })

  it('preserves comments, multiline literals, nested expressions, and quoted identifiers', () => {
    const sql = `CREATE TABLE t (
  [a]]b] INT DEFAULT (coalesce(1, 2)),
  "odd""name" INT NOT NULL,
  commented INT /* outer /* inner ), */ still outer */ NOT NULL,
  note VARCHAR(20) DEFAULT 'line one
line two',
  last INT -- end comment
);`
    const result = alignSqlColumns(sql)
    expect(result).toContain('[a]]b]')
    expect(result).toContain('"odd""name"')
    expect(result).toContain('DEFAULT (coalesce(1, 2))')
    expect(result).toContain('commented INT /* outer /* inner ), */ still outer */ NOT NULL')
    expect(result).toContain("DEFAULT 'line one\nline two'")
    expect(result).toContain('last INT -- end comment\n);')
  })

  it('leaves non-table statements and disabled formatting untouched', () => {
    for (const code of [
      'SELECT name, type FROM t;',
      'CREATE TABLE copy AS SELECT id, name FROM source;',
      '/* sql-formatter-disable */ CREATE TABLE t (a INT, long_name INT); /* sql-formatter-enable */',
    ])
      expect(alignSqlColumns(code)).toBe(code)
  })
})
