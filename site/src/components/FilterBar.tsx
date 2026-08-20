import { useMemo, useRef, useState, type KeyboardEvent } from 'react'
import type { CatalogNode } from '../types'
import {
  FILTER_ATTRIBUTES,
  OPERATOR_LABELS,
  applyFilters,
  describeToken,
  getSuggestedValues,
  newTokenId,
  operatorsFor,
  type FilterAttribute,
  type FilterOperator,
  type FilterToken,
} from '../lib/filters'
import { useDebouncedValue } from '../lib/useDebouncedValue'

type Stage = 'attribute' | 'operator' | 'value'

interface Suggestion {
  key: string
  label: string
  hint?: string
  checked?: boolean
}

interface FilterBarProps {
  nodes: CatalogNode[]
  tokens: FilterToken[]
  onChange: (tokens: FilterToken[]) => void
  placeholder?: string
}

export default function FilterBar({ nodes, tokens, onChange, placeholder }: FilterBarProps) {
  const [stage, setStage] = useState<Stage>('attribute')
  const [attribute, setAttribute] = useState<FilterAttribute | null>(null)
  const [operator, setOperator] = useState<FilterOperator | null>(null)
  const [pendingValues, setPendingValues] = useState<string[]>([])
  const [inputText, setInputText] = useState('')
  const [highlight, setHighlight] = useState(0)
  const [open, setOpen] = useState(false)
  const inputRef = useRef<HTMLInputElement>(null)

  const debouncedInput = useDebouncedValue(inputText, 120)
  const attrDef = FILTER_ATTRIBUTES.find((a) => a.key === attribute)
  const isMultiValue = operator === 'is-in' || operator === 'is-not-in'

  const suggestions: Suggestion[] = useMemo(() => {
    if (stage === 'attribute') {
      const needle = inputText.trim().toLowerCase()
      return FILTER_ATTRIBUTES.filter((a) => !needle || a.label.toLowerCase().includes(needle)).map((a) => ({
        key: a.key,
        label: a.label,
      }))
    }
    if (stage === 'operator' && attribute) {
      const needle = inputText.trim().toLowerCase()
      return operatorsFor(attribute)
        .filter((op) => !needle || OPERATOR_LABELS[op].toLowerCase().includes(needle))
        .map((op) => ({ key: op, label: OPERATOR_LABELS[op] }))
    }
    if (stage === 'value' && attribute && attrDef?.kind === 'enum') {
      return getSuggestedValues(nodes, attribute, debouncedInput, 50).map((v) => ({
        key: v,
        label: v,
        checked: pendingValues.includes(v),
      }))
    }
    return []
  }, [stage, attribute, attrDef, inputText, debouncedInput, nodes, pendingValues])

  function resetDraft() {
    setStage('attribute')
    setAttribute(null)
    setOperator(null)
    setPendingValues([])
    setInputText('')
    setHighlight(0)
  }

  function commit(tok: Omit<FilterToken, 'id'>) {
    onChange([...tokens, { id: newTokenId(), ...tok }])
    resetDraft()
  }

  function pickAttribute(attr: FilterAttribute) {
    setAttribute(attr)
    setStage('operator')
    setInputText('')
    setHighlight(0)
  }

  function pickOperator(op: FilterOperator) {
    setOperator(op)
    setStage('value')
    setInputText('')
    setHighlight(0)
  }

  function pickValue(value: string) {
    if (!attribute || !operator) return
    if (isMultiValue) {
      setPendingValues((prev) => (prev.includes(value) ? prev.filter((v) => v !== value) : [...prev, value]))
      setInputText('')
      return
    }
    commit({ attribute, operator, values: [value] })
  }

  function commitFreeTypedValue() {
    const text = inputText.trim()
    if (!attribute || !operator) {
      if (text) commit({ attribute: null, operator: 'contains', values: [text] })
      return
    }
    if (text) {
      if (isMultiValue) {
        setPendingValues((prev) => (prev.includes(text) ? prev : [...prev, text]))
        setInputText('')
      } else {
        commit({ attribute, operator, values: [text] })
      }
    } else if (isMultiValue && pendingValues.length > 0) {
      commit({ attribute, operator, values: pendingValues })
    }
  }

  function handleKeyDown(e: KeyboardEvent<HTMLInputElement>) {
    if (e.key === 'ArrowDown') {
      e.preventDefault()
      setHighlight((h) => Math.min(h + 1, Math.max(suggestions.length - 1, 0)))
      setOpen(true)
      return
    }
    if (e.key === 'ArrowUp') {
      e.preventDefault()
      setHighlight((h) => Math.max(h - 1, 0))
      return
    }
    if (e.key === 'Enter') {
      e.preventDefault()
      const picked = suggestions[highlight]
      if (open && picked) {
        if (stage === 'attribute') pickAttribute(picked.key as FilterAttribute)
        else if (stage === 'operator') pickOperator(picked.key as FilterOperator)
        else pickValue(picked.key)
      } else {
        commitFreeTypedValue()
      }
      return
    }
    if (e.key === 'Escape') {
      resetDraft()
      inputRef.current?.blur()
      return
    }
    if (e.key === 'Backspace' && inputText === '') {
      if (stage === 'value' && isMultiValue && pendingValues.length > 0) {
        setPendingValues((prev) => prev.slice(0, -1))
        return
      }
      if (stage === 'value') {
        setStage('operator')
        setOperator(null)
        return
      }
      if (stage === 'operator') {
        setStage('attribute')
        setAttribute(null)
        return
      }
      if (tokens.length > 0) {
        onChange(tokens.slice(0, -1))
      }
    }
  }

  const stageHint =
    stage === 'attribute'
      ? placeholder ?? 'Filter by attribute, or type to search...'
      : stage === 'operator'
        ? `${attrDef?.label}...`
        : isMultiValue && pendingValues.length > 0
          ? `${pendingValues.join(', ')} - Enter to apply`
          : `${attrDef?.label} ${operator ? OPERATOR_LABELS[operator] : ''}...`

  return (
    <div className="filter-bar">
      <div className="filter-bar-input-row">
        {tokens.map((token) => (
          <span key={token.id} className="filter-chip">
            {describeToken(token)}
            <button
              type="button"
              className="filter-chip-remove"
              aria-label={`Remove filter ${describeToken(token)}`}
              onClick={() => onChange(tokens.filter((t) => t.id !== token.id))}
            >
              &times;
            </button>
          </span>
        ))}
        {attribute && (
          <span className="filter-chip filter-chip-draft">
            {attrDef?.label}
            {operator ? ` ${OPERATOR_LABELS[operator]}` : ''}
          </span>
        )}
        <input
          ref={inputRef}
          type="text"
          className="filter-bar-input"
          placeholder={stageHint}
          value={inputText}
          onChange={(e) => {
            setInputText(e.target.value)
            setHighlight(0)
          }}
          onFocus={() => setOpen(true)}
          onBlur={() => setTimeout(() => setOpen(false), 150)}
          onKeyDown={handleKeyDown}
        />
      </div>
      {open && suggestions.length > 0 && (
        <ul className="filter-suggestions" role="listbox">
          {suggestions.map((s, i) => (
            <li
              key={s.key}
              role="option"
              aria-selected={i === highlight}
              className={i === highlight ? 'filter-suggestion active' : 'filter-suggestion'}
              onMouseDown={(e) => {
                e.preventDefault()
                if (stage === 'attribute') pickAttribute(s.key as FilterAttribute)
                else if (stage === 'operator') pickOperator(s.key as FilterOperator)
                else pickValue(s.key)
              }}
            >
              {s.checked !== undefined && <span className="filter-suggestion-check">{s.checked ? '✓' : ''}</span>}
              {s.label}
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}

export function useFilteredNodes(nodes: CatalogNode[], tokens: FilterToken[]): CatalogNode[] {
  return useMemo(() => applyFilters(nodes, tokens), [nodes, tokens])
}
