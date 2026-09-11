export const GRAPH_LEGEND_ITEMS = [
  { kind: 'arrow', label: 'Referencing object → referenced object' },
  { kind: 'reference', label: 'Reference' },
  { kind: 'columns', label: 'Labeled edge: recorded column references' },
  { kind: 'dynamic', label: 'Dashed edge: dynamic SQL reference (weaker evidence)' },
  { kind: 'focus', label: 'Current focus' },
  { kind: 'group', label: 'Dashed node: grouped objects' },
] as const

export const GRAPH_LEGEND_NOTE =
  'Node border color indicates object type. Column references are best-effort evidence detected from SQL, not complete column lineage.'
