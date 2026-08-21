import { Routes, Route, NavLink } from 'react-router-dom'
import { CatalogProvider, useCatalog } from './lib/CatalogContext'
import Home from './pages/Home'
import ObjectPage from './pages/ObjectPage'
import LineagePage from './pages/LineagePage'
import Explorer from './pages/Explorer'
import Access from './pages/Access'
import History from './pages/History'

export default function App() {
  return (
    <CatalogProvider>
      <Shell />
    </CatalogProvider>
  )
}

function Shell() {
  const { loading, error } = useCatalog()

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
        <span className="brand">SyncSQL</span>
        <nav>
          <NavLink to="/" end>
            Overview
          </NavLink>
          <NavLink to="/explorer">Explorer</NavLink>
          <NavLink to="/lineage">Lineage</NavLink>
          <NavLink to="/access">Access</NavLink>
          <NavLink to="/history">History</NavLink>
        </nav>
      </header>
      <div className="body">
        <main className="content">
          <Routes>
            <Route path="/" element={<Home />} />
            <Route path="/explorer" element={<Explorer />} />
            <Route path="/object/*" element={<ObjectPage />} />
            <Route path="/lineage" element={<LineagePage />} />
            <Route path="/access" element={<Access />} />
            <Route path="/history" element={<History />} />
          </Routes>
        </main>
      </div>
    </div>
  )
}
