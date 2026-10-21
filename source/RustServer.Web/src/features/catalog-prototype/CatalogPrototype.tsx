import { useEffect, useMemo, useState } from 'react'
import { initialLibrary, loadLibrary, makePlugin, saveLibrary } from './library'
import { ImportDialog } from './ImportDialog'
import { BackupDialog } from './BackupDialog'
import { PluginDetails } from './PluginDetails'
import { LibraryManager } from './LibraryManager'
import { PluginOrganizer } from './PluginOrganizer'
import { PluginCard, PluginRow } from './PluginParts'
import type { LibraryFilters, LibraryPlugin, PrototypeLibrary } from './model'
import styles from './prototype.module.css'

const emptyFilters: LibraryFilters = { site: '', tag: '', folder: '' }
const pageSize = 12
const sorted = (values: string[]) => [...values].sort((a, b) => a.localeCompare(b))

export function CatalogPrototype() {
  const [library, setLibrary] = useState(loadLibrary)
  const [storageError, setStorageError] = useState(false)
  const [query, setQuery] = useState('')
  const [filters, setFilters] = useState<LibraryFilters>(emptyFilters)
  const [activeView, setActiveView] = useState('all')
  const [layout, setLayout] = useState<'table' | 'cards'>('table')
  const [page, setPage] = useState(1)
  const [managing, setManaging] = useState(false)
  const [organizing, setOrganizing] = useState<string | null>(null)
  const [viewName, setViewName] = useState('')
  const [viewError, setViewError] = useState('')
  const [detailsId, setDetailsId] = useState<string | null>(null)
  const [newPlugin, setNewPlugin] = useState<LibraryPlugin | null>(null)
  const [importing, setImporting] = useState(false)
  const [backupMode, setBackupMode] = useState<'export' | 'restore' | null>(null)

  useEffect(() => { setStorageError(!saveLibrary(library)) }, [library])
  const filtered = useMemo(() => library.plugins.filter(plugin => {
    const hasTag = (name: string) => !!name && plugin.tags.some(tag => tag.name === name)
    const inView = activeView === 'purchased' ? hasTag(library.presetTags.purchased)
      : activeView === 'wishlist' ? library.presetTags.wishlist.some(hasTag)
      : activeView === 'archived' ? hasTag(library.presetTags.archived) : true
    return inView && `${plugin.name} ${plugin.author ?? ''}`.toLowerCase().includes(query.trim().toLowerCase()) &&
      (!filters.site || plugin.sources.some(source => source.site === filters.site)) &&
      (!filters.tag || hasTag(filters.tag)) && (!filters.folder || plugin.folders.includes(filters.folder))
  }), [library, query, filters, activeView])
  const pages = Math.max(1, Math.ceil(filtered.length / pageSize))
  const currentPage = Math.min(page, pages)
  const visible = filtered.slice((currentPage - 1) * pageSize, currentPage * pageSize)
  const selectedPlugin = library.plugins.find(plugin => plugin.id === organizing)
  const detailPlugin = newPlugin ?? library.plugins.find(plugin => plugin.id === detailsId)

  const selectView = (value: string) => {
    setActiveView(value); setPage(1)
    const saved = value.startsWith('saved:') ? library.views.find(item => item.name === value.slice(6)) : undefined
    setFilters(saved ? { site: saved.site, tag: saved.tag, folder: saved.folder } : emptyFilters)
  }
  const saveView = () => {
    const name = viewName.trim()
    if (!name || library.views.some(view => view.name.toLowerCase() === name.toLowerCase())) { setViewError('Enter a unique, non-empty view name.'); return }
    setLibrary({ ...library, views: [...library.views, { name, ...filters }] })
    setActiveView(`saved:${name}`); setViewName(''); setViewError('')
  }
  const updateLibrary = (next: PrototypeLibrary) => {
    setLibrary(next)
    // Managed items can be renamed or deleted, so clear transient selections.
    setFilters(emptyFilters); setActiveView('all'); setPage(1)
  }

  if (detailPlugin) return <PluginDetails key={detailPlugin.id} plugin={detailPlugin} library={library} isNew={!!newPlugin} onBack={() => { setDetailsId(null); setNewPlugin(null) }} onSave={(plugin, sites) => { setLibrary({ ...library, plugins: newPlugin ? [...library.plugins, plugin] : library.plugins.map(item => item.id === plugin.id ? plugin : item), sites: [...library.sites, ...sites.filter((site, index) => !library.sites.some(item => item.toLowerCase() === site.toLowerCase()) && sites.findIndex(item => item.toLowerCase() === site.toLowerCase()) === index)] }); setDetailsId(plugin.id); setNewPlugin(null) }} />

  return <main className={styles.page}>
    <header className={styles.header}><div><p className={styles.eyebrow}>Personal library / UI prototype</p><h1>Plugin catalog</h1><p className={styles.intro}>Explore and organize your collection of Rust plugins.</p></div><span className={styles.prototype}>MOCK DATA · STAGE 02</span></header>
    <div className={styles.summary}><strong>{library.plugins.length}</strong><span>plugins in your library</span><span className={styles.summaryNote}>Changes are saved in this browser.</span><button className={styles.primaryAction} type="button" onClick={() => setNewPlugin(makePlugin('', library.plugins))}><span aria-hidden="true">＋</span> Add Plugin</button><details className={styles.actionMenu}><summary className={styles.secondaryAction}><span aria-hidden="true">⇅</span> Import / Export <span aria-hidden="true">▾</span></summary><div><button type="button" onClick={event => { event.currentTarget.closest('details')?.removeAttribute('open'); setImporting(true) }}>Import Plugins (JSON)</button><button type="button" onClick={event => { event.currentTarget.closest('details')?.removeAttribute('open'); setBackupMode('export') }}>Export Catalog (ZIP)</button><button type="button" onClick={event => { event.currentTarget.closest('details')?.removeAttribute('open'); setBackupMode('restore') }}>Restore Catalog (ZIP)</button></div></details><button type="button" onClick={() => setManaging(true)}>Manage library</button><button type="button" onClick={() => { if (window.confirm('Reset the prototype? All local library changes and saved views will be lost.')) { updateLibrary(initialLibrary()); setQuery(''); setOrganizing(null) } }}>Reset</button></div>
    {storageError && <p className={styles.error} role="alert">Browser storage is unavailable. Changes will not persist after reload.</p>}
    <section className={styles.views} aria-label="Library views"><label>View <select value={activeView} onChange={event => selectView(event.target.value)}><option value="all">All plugins</option><option value="purchased">Purchased</option><option value="wishlist">Wishlist</option><option value="archived">Archived</option>{library.views.length > 0 && <optgroup label="Saved views">{library.views.map(view => <option key={view.name} value={`saved:${view.name}`}>{view.name}</option>)}</optgroup>}</select></label><span className={styles.muted}>Wishlist tags: {library.presetTags.wishlist.join(' or ') || 'none'}</span>{activeView.startsWith('saved:') && <button type="button" onClick={() => { if (window.confirm('Delete this saved view?')) { setLibrary({ ...library, views: library.views.filter(view => `saved:${view.name}` !== activeView) }); selectView('all') } }}>Delete view</button>}</section>
    <section aria-label="Catalog filters" className={styles.toolbar}>
      <label className={styles.search}>Search<input type="search" placeholder="Search plugins or authors" value={query} onChange={event => { setQuery(event.target.value); setPage(1) }} /></label>
      {([{ label: 'Source', key: 'site', items: library.sites }, { label: 'Tag', key: 'tag', items: library.tags.map(tag => tag.name) }, { label: 'Folder', key: 'folder', items: library.folders }] as const).map(filter => <label key={filter.key}>{filter.label}<select value={filters[filter.key]} onChange={event => { setFilters({ ...filters, [filter.key]: event.target.value }); setPage(1) }}><option value="">All {filter.label.toLowerCase()}s</option>{sorted(filter.items).map(item => <option key={item}>{item}</option>)}</select></label>)}
    </section>
    <details className={styles.saveView}><summary>Save current filters as a view</summary><form className={styles.inlineForm} onSubmit={event => { event.preventDefault(); saveView() }}><label className={styles.srOnly} htmlFor="view-name">View name</label><input id="view-name" placeholder="View name" value={viewName} onChange={event => setViewName(event.target.value)} /><button>Save view</button></form><p className={styles.muted}>Saves source, tag and folder filters. Search and preset views are not saved.</p>{viewError && <p role="alert" className={styles.error}>{viewError}</p>}</details>
    <div className={styles.resultsBar}><span><strong>{filtered.length}</strong> plugins <span className={styles.muted}>found</span></span><div className={styles.viewSwitch} aria-label="Catalog layout"><button type="button" aria-pressed={layout === 'table'} onClick={() => setLayout('table')}>☰ Table</button><button type="button" aria-pressed={layout === 'cards'} onClick={() => setLayout('cards')}>▦ Cards</button></div></div>
    {visible.length === 0 ? <p className={styles.empty}>No plugins match these filters.</p> : layout === 'table' ? <div className={styles.tableWrap}><table><thead><tr><th>Plugin</th><th>Sources</th><th>Library statuses</th><th>Price</th><th>Folders</th><th><span className={styles.srOnly}>Actions</span></th><th>Last Update</th></tr></thead><tbody>{visible.map(plugin => <PluginRow key={plugin.id} plugin={plugin} onOpen={() => setDetailsId(plugin.id)} onOrganize={() => setOrganizing(plugin.id)} />)}</tbody></table></div> : <div className={styles.cards}>{visible.map(plugin => <PluginCard key={plugin.id} plugin={plugin} onOpen={() => setDetailsId(plugin.id)} onOrganize={() => setOrganizing(plugin.id)} />)}</div>}
    {filtered.length > 0 && <nav className={styles.pagination} aria-label="Catalog pages"><span>Showing {(currentPage - 1) * pageSize + 1}–{Math.min(currentPage * pageSize, filtered.length)} of {filtered.length}</span><div><button type="button" disabled={currentPage === 1} onClick={() => setPage(currentPage - 1)}>Previous</button><span>Page {currentPage} of {pages}</span><button type="button" disabled={currentPage === pages} onClick={() => setPage(currentPage + 1)}>Next</button></div></nav>}
    {managing && <LibraryManager library={library} onChange={updateLibrary} onClose={() => setManaging(false)} />}
    {selectedPlugin && <PluginOrganizer plugin={selectedPlugin} library={library} onChange={plugin => setLibrary({ ...library, plugins: library.plugins.map(item => item.id === plugin.id ? plugin : item) })} onClose={() => setOrganizing(null)} />}
    {importing && <ImportDialog library={library} onImport={setLibrary} onClose={() => setImporting(false)} />}
    {backupMode && <BackupDialog mode={backupMode} library={library} onRestore={next => { if (!saveLibrary(next)) return false; updateLibrary(next); setQuery(''); setOrganizing(null); return true }} onClose={() => setBackupMode(null)} />}
  </main>
}
