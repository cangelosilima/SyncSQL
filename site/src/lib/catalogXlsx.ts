import type { CatalogIndex } from './catalog'
import { qualifiedRefName } from './catalog'
import { columnColumns, dependencyRows, grantColumns, objectColumns, type DependencyRow } from './catalogCsv'
import { sheet, type SheetSpec } from './xlsx'
import type {
  CatalogLinkedServerReference,
  CatalogMetricSnapshot,
  CatalogNode,
  CatalogObjectVersion,
  CatalogOrphanedReference,
  CatalogSection,
  CatalogSystemReference,
} from '../types'

/**
 * One object's whole detail page as a workbook: a worksheet per section, in the
 * order the page shows them.
 *
 * A single CSV can only ever be one of these, which is why the page grew a
 * button per section. A workbook is the shape the question actually has - "give
 * me everything about this object" - and keeping one worksheet per section means
 * a reader who knows the page already knows the file.
 *
 * Column definitions are reused from catalogCsv.ts wherever a CSV export already
 * covers the same rows, so the two can't drift.
 */

/** A one-row section (the object's own facts) reads better transposed - one row per property. */
interface PropertyRow {
  property: string
  value: string | number | null
}

const propertyColumns = [
  { header: 'Property', value: (row: PropertyRow) => row.property },
  { header: 'Value', value: (row: PropertyRow) => row.value },
]

/** A reference the DDL makes, as the four "where it pointed" columns plus the name. */
const referenceColumns = [
  { header: 'Reference', value: (ref: { server?: string | null; database?: string | null; schema: string | null; name: string }) => qualifiedRefName(ref) },
  { header: 'Server', value: (ref: { server?: string | null }) => ref.server ?? null },
  { header: 'Database', value: (ref: { database?: string | null }) => ref.database ?? null },
  { header: 'Schema', value: (ref: { schema: string | null }) => ref.schema },
  { header: 'Name', value: (ref: { name: string }) => ref.name },
]

const orphanedReferenceColumns = referenceColumns as { header: string; value: (row: CatalogOrphanedReference) => string | null }[]
const systemReferenceColumns = referenceColumns as { header: string; value: (row: CatalogSystemReference) => string | null }[]

function linkedServerReferenceColumns(index: CatalogIndex) {
  return [
    {
      header: 'Through',
      value: (ref: CatalogLinkedServerReference) => index.byId.get(ref.linkedServer)?.qualifiedName ?? ref.linkedServer,
    },
    { header: 'RemoteObject', value: (ref: CatalogLinkedServerReference) => qualifiedRefName(ref) },
    { header: 'InCatalog', value: (ref: CatalogLinkedServerReference) => (ref.to ? 'yes' : 'not extracted') },
    { header: 'TargetId', value: (ref: CatalogLinkedServerReference) => ref.to },
    { header: 'Dynamic', value: (ref: CatalogLinkedServerReference) => (ref.dynamic ? 'yes' : 'no') },
  ]
}

const historyColumns = [
  { header: 'Date', value: (version: CatalogObjectVersion) => version.date },
  { header: 'Sha', value: (version: CatalogObjectVersion) => version.sha },
  { header: 'Message', value: (version: CatalogObjectVersion) => version.message },
  { header: 'DefinitionAvailable', value: (version: CatalogObjectVersion) => (version.ddl ? 'yes' : 'no') },
]

const metricColumns = [
  { header: 'CapturedAt', value: (m: CatalogMetricSnapshot) => m.capturedAt },
  { header: 'RowCount', value: (m: CatalogMetricSnapshot) => m.rowCount },
  { header: 'ReservedKB', value: (m: CatalogMetricSnapshot) => m.reservedKB },
  { header: 'DataKB', value: (m: CatalogMetricSnapshot) => m.dataKB },
  { header: 'IndexKB', value: (m: CatalogMetricSnapshot) => m.indexKB },
  { header: 'Indexes', value: (m: CatalogMetricSnapshot) => m.indexes.length },
  { header: 'Statistics', value: (m: CatalogMetricSnapshot) => m.statistics.length },
]

const sectionColumns = [
  { header: 'Section', value: (section: CatalogSection) => section.title },
  { header: 'Content', value: (section: CatalogSection) => section.content },
]

/** The DDL one row per line, because a whole definition in a single cell is unreadable in Excel. */
interface DdlLine {
  line: number
  text: string
}

const ddlColumns = [
  { header: 'Line', value: (row: DdlLine) => row.line },
  { header: 'Text', value: (row: DdlLine) => row.text },
]

/** Dependencies split by direction, so each side gets its own worksheet as the page does. */
const dependencyColumnsForSheet = [
  { header: 'Id', value: (row: DependencyRow) => row.node.id },
  { header: 'QualifiedName', value: (row: DependencyRow) => row.node.qualifiedName },
  { header: 'Type', value: (row: DependencyRow) => row.node.type },
  { header: 'Server', value: (row: DependencyRow) => row.node.server },
  { header: 'Database', value: (row: DependencyRow) => row.node.database },
  { header: 'Schema', value: (row: DependencyRow) => row.node.schema },
  { header: 'ReferencedColumns', value: (row: DependencyRow) => row.columns.join(' ') },
]

/**
 * Every worksheet for one object, empty sections skipped - a workbook full of
 * blank tabs is worse than a short one. "Details" is always present, so the file
 * is never empty.
 */
export function objectWorkbookSheets(index: CatalogIndex, node: CatalogNode): SheetSpec[] {
  const sheets: SheetSpec[] = []

  const properties: PropertyRow[] = objectColumns(index).map((column) => ({
    property: column.header,
    value: (column.value(node) ?? null) as string | number | null,
  }))
  sheets.push(sheet('Details', properties, propertyColumns))

  if (node.columns.length > 0) {
    sheets.push(sheet('Columns', node.columns, columnColumns))
  }
  if (node.grants.length > 0) {
    sheets.push(sheet('Access', node.grants, grantColumns))
  }

  const dependencies = dependencyRows(index, node.id)
  const dependsOn = dependencies.filter((row) => row.direction === 'Depends on')
  const usedBy = dependencies.filter((row) => row.direction === 'Used by')
  if (dependsOn.length > 0) {
    sheets.push(sheet('Depends on', dependsOn, dependencyColumnsForSheet))
  }
  if (usedBy.length > 0) {
    sheets.push(sheet('Used by', usedBy, dependencyColumnsForSheet))
  }

  const orphaned = index.orphanedByFrom.get(node.id) ?? []
  if (orphaned.length > 0) {
    sheets.push(sheet('Orphaned refs', orphaned, orphanedReferenceColumns))
  }

  const systemRefs = index.systemRefsByFrom.get(node.id) ?? []
  if (systemRefs.length > 0) {
    sheets.push(sheet('System refs', systemRefs, systemReferenceColumns))
  }

  // Both directions of the link story: what this object reaches through links,
  // and - for a link object itself - everything the fleet reaches through it.
  const acrossLinks = index.linkedServerRefsByFrom.get(node.id) ?? []
  if (acrossLinks.length > 0) {
    sheets.push(sheet('Across linked servers', acrossLinks, linkedServerReferenceColumns(index)))
  }
  const throughThisLink = index.linkedServerRefsByLink.get(node.id) ?? []
  if (throughThisLink.length > 0) {
    sheets.push(sheet('Through this link', throughThisLink, linkedServerReferenceColumns(index)))
  }

  if (node.metrics.length > 0) {
    sheets.push(sheet('Metrics', node.metrics, metricColumns))
  }
  if (node.history.length > 0) {
    // Named as the page names the section - and "History" alone is a name Excel
    // reserves for shared-workbook change tracking and refuses outright.
    sheets.push(sheet('Change history', node.history, historyColumns))
  }
  if (node.ddl) {
    const lines: DdlLine[] = node.ddl.split('\n').map((text, i) => ({ line: i + 1, text }))
    sheets.push(sheet('Definition', lines, ddlColumns))
  }
  if (node.sections.length > 0) {
    sheets.push(sheet('Sections', node.sections, sectionColumns))
  }

  return sheets
}
