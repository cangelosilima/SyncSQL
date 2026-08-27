interface ContentSearchBarProps {
  value: string
  onChange: (value: string) => void
  placeholder?: string
  matchCount?: number
}

/**
 * A live (search-as-you-type) text box for full-text search over DDL bodies,
 * separate from FilterBar's attribute-based filters - the caller is
 * expected to debounce `value` before actually filtering with it since this
 * scans object bodies rather than short indexed fields.
 */
export default function ContentSearchBar({ value, onChange, placeholder, matchCount }: ContentSearchBarProps) {
  return (
    <div className="content-search">
      <input
        type="text"
        className="content-search-input"
        placeholder={placeholder ?? 'Search DDL content... (e.g. a column or table name referenced in the body)'}
        value={value}
        onChange={(e) => onChange(e.target.value)}
      />
      {value.trim() && matchCount !== undefined && (
        <span className="content-search-count">
          {matchCount} match{matchCount === 1 ? '' : 'es'}
        </span>
      )}
      {value.trim() && (
        <button type="button" className="content-search-clear" aria-label="Clear DDL search" onClick={() => onChange('')}>
          &times;
        </button>
      )}
    </div>
  )
}
