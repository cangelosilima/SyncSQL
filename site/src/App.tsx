import { Routes, Route, NavLink } from 'react-router-dom'
import { CatalogProvider, useCatalog } from './lib/CatalogContext'
import { useTheme } from './lib/ThemeContext'
import Home from './pages/Home'
import ObjectPage from './pages/ObjectPage'
import LineagePage from './pages/LineagePage'
import Explorer from './pages/Explorer'
import History from './pages/History'
import pkg from '../package.json'

export default function App() {
  return (
    <CatalogProvider>
      <Shell />
    </CatalogProvider>
  )
}

function Shell() {
  const { loading, error, index } = useCatalog()

  if (loading) {
    return (
      <div className="center-screen">
        <p>Loading catalog...</p>
      </div>
    )
  }

  if (error) {
    return (
      <div className="center-screen">
        <p className="error-text">Failed to load catalog: {error}</p>
        <p className="muted">
          This page expects data/catalog.json to be published alongside the site by the analyze-catalog CI job.
        </p>
      </div>
    )
  }

  return (
    <div className="layout">
      <header className="topbar">
        <span className="brand">
          <span className="brand-mark">SQL</span>
          <span className="brand-name">SyncSQL</span>
          <span className="brand-version">v{pkg.version}</span>
        </span>
        <nav>
          <NavLink to="/" end>
            Overview
          </NavLink>
          <NavLink to="/explorer">Explorer</NavLink>
          <NavLink to="/lineage">Lineage</NavLink>
          <NavLink to="/history">History</NavLink>
        </nav>
        <div className="topbar-status">
          {(index?.catalog.servers ?? []).map((server) => (
            <span key={server} className="status-pill" title={`Server: ${server}`}>
              <span className="status-dot" />
              <span className="status-pill-name">{server}</span>
            </span>
          ))}
          <span className="status-pill" title="Catalog data is a static snapshot published by the analyze-catalog CI job">
            <span className="status-dot" />
            Synced
          </span>
          <ThemeToggle />
        </div>
      </header>
      <div className="body">
        <main className="content">
          <Routes>
            <Route path="/" element={<Home />} />
            <Route path="/explorer" element={<Explorer />} />
            <Route path="/object/*" element={<ObjectPage />} />
            <Route path="/lineage" element={<LineagePage />} />
            <Route path="/history" element={<History />} />
          </Routes>
        </main>
      </div>
    </div>
  )
}

function ThemeToggle() {
  const { theme, toggleTheme } = useTheme()
  return (
    <button type="button" className="theme-toggle" onClick={toggleTheme} aria-label={`Switch to ${theme === 'light' ? 'dark' : 'light'} theme`}>
      {theme === 'light' ? '☀ Light' : '☾ Dark'}
    </button>
  )
}
