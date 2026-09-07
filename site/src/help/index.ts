import overview from './overview.md?raw'
import explorer from './explorer.md?raw'
import ai from './ai.md?raw'
import lineage from './lineage.md?raw'
import history from './history.md?raw'
import object from './object.md?raw'
import alerts from './alerts.md?raw'

/**
 * Per-page guidance, authored as Markdown next to this file and inlined into
 * the bundle at build time (`?raw`). Bundling rather than fetching keeps help
 * working on a Pages deployment served from an unknown subpath, and offline
 * once the site has loaded.
 */
export type HelpTopic = 'overview' | 'explorer' | 'ai' | 'lineage' | 'history' | 'object' | 'alerts'

export interface HelpGuide {
  /** The document's own top-level heading, rendered as the panel title. */
  title: string
  /** The document minus that heading - the panel renders the title itself. */
  body: string
}

/**
 * Splits a guide's leading `# Heading` off its body so the panel can show it
 * as a real title without the document repeating itself. The `.md` files stay
 * complete, readable documents on their own.
 */
export function toGuide(source: string, fallbackTitle: string): HelpGuide {
  const match = /^\s*#\s+(.+)\r?\n?/.exec(source)
  if (!match) return { title: fallbackTitle, body: source.trim() }
  return { title: match[1].trim(), body: source.slice(match[0].length).trim() }
}

export const helpGuides: Record<HelpTopic, HelpGuide> = {
  overview: toGuide(overview, 'Overview'),
  explorer: toGuide(explorer, 'Explorer'),
  ai: toGuide(ai, 'AI filter assistant'),
  lineage: toGuide(lineage, 'Lineage explorer'),
  history: toGuide(history, 'History'),
  object: toGuide(object, 'Object detail'),
  alerts: toGuide(alerts, 'Alerts'),
}
