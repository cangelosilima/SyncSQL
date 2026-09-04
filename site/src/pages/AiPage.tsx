import { useEffect, useMemo, useRef, useState, type FormEvent } from 'react'
import { Link } from 'react-router-dom'
import { useAi } from '../ai/AiContext'
import type { FilterPlanV1 } from '../ai/types'
import { useCatalog } from '../lib/CatalogContext'
import { filterByContent } from '../lib/contentSearch'
import { applyFilters, describeToken, encodeTokensForUrl, type FilterToken } from '../lib/filters'

const EXAMPLES = [
  'Show stored procedures in AppDb that mention Orders',
  'Find tables on SQLPROD01 except schema dbo',
  'Objects named Customer that reference invoices',
]

export default function AiPage() {
  const { index } = useCatalog()
  const ai = useAi()
  const [query, setQuery] = useState('')
  const [plan, setPlan] = useState<FilterPlanV1 | null>(null)
  const [localError, setLocalError] = useState<string | null>(null)
  const controllerRef = useRef<AbortController | null>(null)

  useEffect(() => () => controllerRef.current?.abort(), [])

  const nodes = useMemo(() => index?.catalog.nodes ?? [], [index])
  const previewTokens = useMemo<FilterToken[]>(() => plan?.tokens.map((token, position) => ({ ...token, id: `ai-preview-${position}` })) ?? [], [plan])
  const matchCount = useMemo(() => {
    if (!plan) return null
    return filterByContent(applyFilters(nodes, previewTokens), plan.contentQuery).length
  }, [nodes, plan, previewTokens])
  const explorerUrl = useMemo(() => {
    if (!plan) return '/explorer'
    const params = new URLSearchParams()
    if (plan.tokens.length > 0) params.set('filters', encodeTokensForUrl(plan.tokens))
    if (plan.contentQuery) params.set('q', plan.contentQuery)
    const suffix = params.toString()
    return suffix ? `/explorer?${suffix}` : '/explorer'
  }, [plan])

  if (ai.checking) {
    return <StatusPage title="AI" message="Checking whether the local model is included in this deployment..." />
  }

  if (!ai.available) {
    return (
      <div className="page ai-page">
        <h1>AI</h1>
        <div className="ai-unavailable" role="status">
          <h2>AI filter generation is unavailable</h2>
          <p>{availabilityMessage(ai.reason)}</p>
          <p className="muted">Explorer and every other catalog feature remain available.</p>
          {ai.reason === 'runtime-error' && ai.error && <p className="error-text">Details: {ai.error}</p>}
          {ai.reason === 'runtime-error' && (
            <button type="button" className="ai-primary-button" onClick={ai.retry}>Retry local model</button>
          )}
        </div>
      </div>
    )
  }

  const busy = ai.runtimeStatus === 'loading' || ai.runtimeStatus === 'running'
  const canOpen = plan
    ? (plan.tokens.length > 0 || Boolean(plan.contentQuery)) && plan.unsupportedFragments.length === 0
    : false

  async function submit(event: FormEvent) {
    event.preventDefault()
    if (!query.trim() || busy) return
    controllerRef.current?.abort()
    const controller = new AbortController()
    controllerRef.current = controller
    setLocalError(null)
    setPlan(null)
    try {
      setPlan(await ai.generateFilterPlan(query, nodes, controller.signal))
    } catch (error) {
      if (!(error instanceof DOMException && error.name === 'AbortError')) {
        setLocalError(error instanceof Error ? error.message : String(error))
      }
    }
  }

  function cancel() {
    controllerRef.current?.abort()
    controllerRef.current = null
  }

  return (
    <div className="page ai-page">
      <div className="ai-heading-row">
        <div>
          <h1>AI</h1>
          <p className="muted">Describe the objects you want to find. Interpretation and filtering stay in this browser.</p>
        </div>
        <span className="ai-local-badge">Local · English</span>
      </div>

      <form className="ai-prompt" onSubmit={submit}>
        <label htmlFor="ai-filter-query">Filter request</label>
        <textarea
          id="ai-filter-query"
          value={query}
          onChange={(event) => setQuery(event.target.value)}
          placeholder="Show stored procedures in AppDb that mention Orders"
          rows={4}
          disabled={busy}
        />
        <div className="ai-examples" aria-label="Example filter requests">
          {EXAMPLES.map((example) => (
            <button key={example} type="button" onClick={() => setQuery(example)} disabled={busy}>{example}</button>
          ))}
        </div>
        <div className="ai-actions">
          <button type="submit" className="ai-primary-button" disabled={busy || !query.trim()}>
            {busy ? 'Working...' : 'Generate filters'}
          </button>
          {busy && <button type="button" className="ai-secondary-button" onClick={cancel}>Cancel</button>}
          <span className="muted">The local model is loaded on the first request.</span>
        </div>
      </form>

      <div className="ai-runtime-status" aria-live="polite">
        {ai.runtimeStatus === 'loading' && (
          <>
            <span>Loading local model{ai.progress?.file ? `: ${ai.progress.file}` : '...'}</span>
            {ai.progress?.percent !== null && ai.progress?.percent !== undefined && (
              <progress max="100" value={ai.progress.percent}>{Math.round(ai.progress.percent)}%</progress>
            )}
          </>
        )}
        {ai.runtimeStatus === 'running' && <span>Interpreting request...</span>}
        {(localError || ai.error) && <span className="error-text">{localError ?? ai.error}</span>}
      </div>

      {plan && (
        <section className="ai-preview" aria-labelledby="ai-preview-title">
          <div className="ai-preview-header">
            <div>
              <h2 id="ai-preview-title">Filter preview</h2>
              <p className="muted">{matchCount} of {nodes.length} object(s) match</p>
            </div>
            <span className={`ai-confidence ai-confidence--${plan.confidence}`}>{plan.confidence} confidence</span>
          </div>

          <div className="ai-filter-chips">
            {previewTokens.map((token) => <span key={token.id} className="filter-chip">{describeToken(token)}</span>)}
            {plan.contentQuery && <span className="filter-chip">DDL contains {plan.contentQuery}</span>}
            {previewTokens.length === 0 && !plan.contentQuery && <span className="muted">No safe filters were generated.</span>}
          </div>

          {plan.warnings.length > 0 && (
            <ul className="ai-warning-list">
              {plan.warnings.map((warning) => <li key={warning}>{warning}</li>)}
            </ul>
          )}
          {plan.unsupportedFragments.length > 0 && (
            <p className="muted">Rewrite the unsupported part before opening Explorer: {plan.unsupportedFragments.join('; ')}</p>
          )}

          {canOpen
            ? <Link className="ai-primary-link" to={explorerUrl}>Open in Explorer</Link>
            : <button type="button" className="ai-primary-button" disabled>Open in Explorer</button>}
        </section>
      )}
    </div>
  )
}

function StatusPage({ title, message }: { title: string; message: string }) {
  return <div className="page ai-page"><h1>{title}</h1><p className="muted" role="status">{message}</p></div>
}

function availabilityMessage(reason: string | null): string {
  switch (reason) {
    case 'model-missing': return 'The local model was not included in this site deployment.'
    case 'lfs-unresolved': return 'The local model was not available when this site was built.'
    case 'checksum-mismatch': return 'The local model did not pass integrity verification during deployment.'
    case 'packaging-failed': return 'The local model could not be packaged with this deployment.'
    case 'runtime-error': return 'The browser could not initialize the local model.'
    default: return 'This deployment does not advertise a usable local model.'
  }
}
