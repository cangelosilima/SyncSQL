import { useEffect } from 'react'
import { Routes, Route, NavLink, useLocation } from 'react-router-dom'
import { CatalogProvider, useCatalog } from './lib/CatalogContext'
import { useTheme } from './lib/ThemeContext'
import Home from './pages/Home'
import ObjectPage from './pages/ObjectPage'
import LineagePage from './pages/LineagePage'
import Explorer from './pages/Explorer'
import History from './pages/History'
import AiPage from './pages/AiPage'
import Alerts from './pages/Alerts'
import { AiProvider, useAi } from './ai/AiContext'
import pkg from '../package.json'
import Button from './components/Button'
import CatalogSidebar from './components/CatalogSidebar'

export default function App() {
  return (
    <CatalogProvider>
      <AiProvider>
        <Shell />
      </AiProvider>
    </CatalogProvider>
  )
}

function Shell() {
  const { loading, error, index } = useCatalog()
  const ai = useAi()
  const { pathname } = useLocation()
  useEffect(() => { document.getElementById('main-content')?.scrollTo?.({ top: 0 }) }, [pathname])

  if (loading) {
    return (
      <div className="center-screen">
        <p role="status">Loading catalog...</p>
      </div>
    )
  }

  if (error) {
    return (
      <div className="center-screen">
        <p className="error-text" role="alert">Failed to load catalog: {error}</p>
        <p className="muted">
          This page expects data/catalog.json to be published alongside the site by the analyze-catalog CI job.
        </p>
      </div>
    )
  }

  return (
    <div className="layout">
      <a className="skip-link" href="#main-content" onClick={(event) => { event.preventDefault(); document.getElementById('main-content')?.focus() }}>Skip to content</a>
      <header className="topbar">
        <span className="brand">
          <img className="brand-icon" src={`${import.meta.env.BASE_URL}sqlineage-icon.svg`} alt="" width="32" height="32" />
          <span className="brand-name">SQLineage</span>
          <span className="brand-version">v{pkg.version}</span>
          {index?.catalog.example && <span className="sync-line-badge">Example catalog</span>}
        </span>
        <nav aria-label="Primary">
          <NavLink to="/" end>
            Overview
          </NavLink>
          <NavLink to="/explorer">Explorer</NavLink>
          <NavLink to="/lineage">Lineage</NavLink>
          <NavLink to="/alerts">Alerts</NavLink>
          {ai.available
            ? <NavLink to="/ai">AI</NavLink>
            : (
              <span
                className="nav-link-disabled"
                aria-disabled="true"
                title={ai.checking
                  ? 'Checking AI availability'
                  : ai.reason === 'runtime-error'
                    ? 'AI disabled after the local model failed to load'
                    : 'AI model not included in this deployment'}
              >
                AI
              </span>
            )}
          <NavLink to="/history">History</NavLink>
        </nav>
        <div className="topbar-status">
          <span className="status-pill" title="Catalog data is a static snapshot published by the analyze-catalog CI job">
            Snapshot · {index && new Date(index.catalog.generatedAt).toLocaleString()}
          </span>
          <ThemeToggle />
        </div>
      </header>
      <div className="body">
        {(pathname.replace(/\/$/, '') === '/explorer' || pathname.startsWith('/object/')) && <CatalogSidebar nodes={index?.catalog.nodes ?? []} linkedServerReferences={index?.catalog.linkedServerReferences} />}
        <main className="content" id="main-content" tabIndex={-1}>
          <Routes>
            <Route path="/" element={<Home />} />
            <Route path="/explorer" element={<Explorer />} />
            <Route path="/ai" element={<AiPage />} />
            <Route path="/alerts" element={<Alerts />} />
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
    <Button className="theme-toggle" onClick={toggleTheme} aria-label={`Switch to ${theme === 'light' ? 'dark' : 'light'} theme`}>
      {theme === 'light' ? '☀ Light' : '☾ Dark'}
    </Button>
  )
}
