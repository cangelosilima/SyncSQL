export default function CatalogLoadStatus({ loading, error }: { loading: boolean; error?: string }) {
  if (error)
    return (
      <p className="error-text" role="alert">
        {error}{' '}
        <button type="button" onClick={() => window.location.reload()}>
          Reload catalog
        </button>
      </p>
    )
  if (loading) return <p role="status">Loading catalog data...</p>
  return null
}
