const PALETTE: Record<string, string> = {
  Tables: '#3b82f6',
  Views: '#8b5cf6',
  StoredProcedures: '#f59e0b',
  Procedures: '#f59e0b',
  Functions: '#10b981',
  Triggers: '#ef4444',
  Synonyms: '#06b6d4',
  Schemas: '#6b7280',
  LinkedServers: '#ec4899',
  DatabaseLinks: '#ec4899',
  Packages: '#14b8a6',
  PackageBodies: '#0d9488',
}

const FALLBACK = '#9ca3af'

export function colorForType(type: string): string {
  return PALETTE[type] ?? FALLBACK
}
