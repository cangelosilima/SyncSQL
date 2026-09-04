import type { CatalogIndex } from './catalog'
import { getEdgeColumns } from './neighborhood'
import type { CsvColumn } from './csv'
import type { CatalogColumn, CatalogGrant, CatalogNode } from '../types'

/**
 * The column sets every "Export CSV" button in the site writes, kept in one
 * place so the same object exported from the explorer and from its own detail
 * page produces the same headers - a spreadsheet someone built a pivot on
 * shouldn't change shape depending on which button produced it.
 */

/** One catalog object per row: identity, description, size, and how connected it is. */
export function objectColumns(index: CatalogIndex): CsvColumn<CatalogNode>[] {
  return [
    { header: 'Id', value: (node) => node.id },
    { header: 'Server', value: (node) => node.server },
    { header: 'Database', value: (node) => node.database },
    { header: 'Schema', value: (node) => node.schema },
    { header: 'Type', value: (node) => node.type },
    { header: 'Name', value: (node) => node.name },
    { header: 'QualifiedName', value: (node) => node.qualifiedName },
    { header: 'Description', value: (node) => node.description },
    { header: 'Path', value: (node) => node.path },
    { header: 'SizeBytes', value: (node) => node.sizeBytes },
    { header: 'ColumnCount', value: (node) => node.columns.length },
    { header: 'GrantCount', value: (node) => node.grants.length },
    { header: 'DependsOnCount', value: (node) => index.outgoing.get(node.id)?.length ?? 0 },
    { header: 'UsedByCount', value: (node) => index.incoming.get(node.id)?.length ?? 0 },
    { header: 'ChangeCount', value: (node) => node.changeCount },
    { header: 'LastChangedAt', value: (node) => node.lastChangedAt },
  ]
}

export const columnColumns: CsvColumn<CatalogColumn>[] = [
  { header: 'Column', value: (column) => column.name },
  { header: 'DataType', value: (column) => column.dataType },
  { header: 'Description', value: (column) => column.description },
]

export const grantColumns: CsvColumn<CatalogGrant>[] = [
  { header: 'Grantee', value: (grant) => grant.grantee },
  { header: 'GranteeType', value: (grant) => grant.granteeType },
  { header: 'Permission', value: (grant) => grant.permission },
  { header: 'State', value: (grant) => grant.state },
  { header: 'Column', value: (grant) => grant.column },
]

/** A grant exported with the object it sits on - what the access search returns. */
export interface ObjectGrantRow {
  node: CatalogNode
  grant: CatalogGrant
}

export const objectGrantColumns: CsvColumn<ObjectGrantRow>[] = [
  { header: 'Id', value: (row) => row.node.id },
  { header: 'Server', value: (row) => row.node.server },
  { header: 'Database', value: (row) => row.node.database },
  { header: 'Schema', value: (row) => row.node.schema },
  { header: 'Type', value: (row) => row.node.type },
  { header: 'QualifiedName', value: (row) => row.node.qualifiedName },
  ...grantColumns.map((column) => ({ header: column.header, value: (row: ObjectGrantRow) => column.value(row.grant) })),
]

export type DependencyDirection = 'Depends on' | 'Used by'

export interface DependencyRow {
  direction: DependencyDirection
  node: CatalogNode
  /** Best-effort column-level tags for this edge - the same signal the lineage lists show. */
  columns: string[]
}

export const dependencyColumns: CsvColumn<DependencyRow>[] = [
  { header: 'Direction', value: (row) => row.direction },
  { header: 'Id', value: (row) => row.node.id },
  { header: 'Server', value: (row) => row.node.server },
  { header: 'Database', value: (row) => row.node.database },
  { header: 'Schema', value: (row) => row.node.schema },
  { header: 'Type', value: (row) => row.node.type },
  { header: 'QualifiedName', value: (row) => row.node.qualifiedName },
  { header: 'ReferencedColumns', value: (row) => row.columns.join(' ') },
]

/**
 * Both directions of one object's lineage as flat rows - dependencies first,
 * then dependents, each in the order the page lists them.
 */
export function dependencyRows(index: CatalogIndex, rootId: string): DependencyRow[] {
  const rows: DependencyRow[] = []
  for (const id of index.outgoing.get(rootId) ?? []) {
    const node = index.byId.get(id)
    if (node) rows.push({ direction: 'Depends on', node, columns: getEdgeColumns(index, rootId, id) })
  }
  for (const id of index.incoming.get(rootId) ?? []) {
    const node = index.byId.get(id)
    if (node) rows.push({ direction: 'Used by', node, columns: getEdgeColumns(index, id, rootId) })
  }
  return rows
}
