import { useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { getPluginCatalogFolders, getPluginCatalogSites, getPluginCatalogTags, resolvePluginCatalogContentUrl, type FolderDto, type SiteDto, type TagDto, type PluginSummaryDto } from '../../../shared/api'
import { PluginCard, PluginRow } from '../../catalog-prototype/PluginParts'
import type { LibraryPlugin } from '../../catalog-prototype/model'
import styles from '../../catalog-prototype/prototype.module.css'
import { PluginsSectionNavigation } from '../components/PluginsSectionNavigation'
import { pluginCatalogApi, type PluginCatalogApi } from './api'
import { PluginEditor } from './PluginEditor'
import { LibraryManager } from './LibraryManager'
import { ImportDialog } from './ImportDialog'
import { ExportDialog } from './ExportDialog'
import { RestoreDialog } from './RestoreDialog'

const pageSize = 12
interface CatalogPageProps { api?: Pick<PluginCatalogApi, 'listPlugins'>; getSites?: typeof getPluginCatalogSites; getTags?: typeof getPluginCatalogTags; getFolders?: typeof getPluginCatalogFolders }
type Result = Awaited<ReturnType<PluginCatalogApi['listPlugins']>>
type Options = { sites: SiteDto[]; tags: TagDto[]; folders: FolderDto[] }

function toLibraryPlugin(plugin: PluginSummaryDto, sites: SiteDto[]): LibraryPlugin {
  return {
    id: plugin.id, slug: plugin.slug, name: plugin.name,
    author: plugin.author ?? undefined, description: plugin.shortDescription || undefined,
    iconUrl: plugin.icon ? resolvePluginCatalogContentUrl(plugin.icon.thumbnailAccessUrl) : undefined,
    lastUpdate: plugin.lastPublishedDate ?? undefined,
    sources: plugin.sources.map(source => ({ site: sites.find(site => site.id === source.siteId)?.name ?? source.label ?? 'Website', url: source.url })),
    tags: plugin.tags, folders: plugin.folders.map(folder => folder.name),
    price: plugin.price !== null && plugin.currency !== null ? { amount: Number(plugin.price), currency: plugin.currency } : undefined,
  }
}

export function CatalogPage({ api = pluginCatalogApi, getSites = getPluginCatalogSites, getTags = getPluginCatalogTags, getFolders = getPluginCatalogFolders }: CatalogPageProps) {
  const navigate = useNavigate()
  const [importing, setImporting] = useState(false)
  const [exporting, setExporting] = useState(false)
  const [restoring, setRestoring] = useState(false)
  const [creating, setCreating] = useState(false)
  const [managing, setManaging] = useState<Options | null>(null)
  const [input, setInput] = useState('')
  const [search, setSearch] = useState('')
  const [siteId, setSiteId] = useState('')
  const [tagId, setTagId] = useState('')
  const [folderId, setFolderId] = useState('')
  const [page, setPage] = useState(1)
  const [layout, setLayout] = useState<'table' | 'cards'>('table')
  const [refresh, setRefresh] = useState(0)
  const [options, setOptions] = useState<Options | null>(null)
  const [result, setResult] = useState<Result | null>(null)
  const [error, setError] = useState(false)

  useEffect(() => {
    const timer = window.setTimeout(() => { setSearch(input.trim()) }, 300)
    return () => window.clearTimeout(timer)
  }, [input])

  useEffect(() => {
    const controller = new AbortController()
    // oxlint-disable-next-line react/set-state-in-effect -- discard data from the previous query
    setResult(null); setOptions(null); setError(false)
    void Promise.all([
      api.listPlugins({ page, pageSize, search, ...(siteId && { siteId }), ...(tagId && { tagId }), ...(folderId && { folderId }) }, controller.signal),
      getSites(controller.signal), getTags(controller.signal), getFolders(controller.signal),
    ]).then(([data, sites, tags, folders]) => {
      if (!controller.signal.aborted) { setResult(data); setOptions({ sites, tags, folders }) }
    }).catch(() => { if (!controller.signal.aborted) setError(true) })
    return () => controller.abort()
  }, [api, getSites, getTags, getFolders, page, search, siteId, tagId, folderId, refresh])

  const filters = [
    { label: 'Site', value: siteId, set: setSiteId, items: options?.sites ?? [] },
    { label: 'Tag', value: tagId, set: setTagId, items: options?.tags ?? [] },
    { label: 'Folder', value: folderId, set: setFolderId, items: options?.folders ?? [] },
  ]
  const plugins = result && options ? result.items.map(plugin => toLibraryPlugin(plugin, options.sites)) : []
  if (creating) return <main className={styles.page}><PluginEditor onCancel={() => setCreating(false)} onComplete={plugin => navigate(`/plugins/catalog/${encodeURIComponent(plugin.id)}`)} /></main>
  return <main className={styles.page}>
    <header className={styles.header}><div><p className={styles.eyebrow}>Plugins</p><h1>Plugin catalog</h1><p className={styles.intro}>Explore known plugin catalog metadata.</p></div></header>
    <PluginsSectionNavigation active="catalog" />
    <div className={styles.summary}><button className={styles.primaryAction} type="button" onClick={() => setCreating(true)}>＋ Add Plugin</button><button type="button" disabled={!options} onClick={() => setManaging(options)}>Manage library</button><button type="button" onClick={() => setImporting(true)}>Import JSON</button><button type="button" onClick={() => setExporting(true)}>Export Catalog (ZIP)</button><button type="button" onClick={() => setRestoring(true)}>Restore Catalog (ZIP)</button></div>
    {importing && <ImportDialog onClose={() => setImporting(false)} onImported={() => { setPage(1); setRefresh(value => value + 1) }} />}
    {exporting && <ExportDialog onClose={() => setExporting(false)} />}
    {restoring && <RestoreDialog onClose={() => setRestoring(false)} onRestored={() => { setPage(1); setRefresh(value => value + 1) }} />}
    {managing && <LibraryManager initial={managing} onClose={() => setManaging(null)} onChanged={deleted => { if (deleted?.kind === 'sites' && deleted.id === siteId) setSiteId(''); if (deleted?.kind === 'tags' && deleted.id === tagId) setTagId(''); if (deleted?.kind === 'folders' && deleted.id === folderId) setFolderId(''); setPage(1); setRefresh(value => value + 1) }} />}
    <section aria-label="Catalog filters" className={styles.toolbar}>
      <label className={styles.search}>Search<input type="search" placeholder="Search plugins or authors" value={input} onChange={event => { setInput(event.target.value); setPage(1) }} /></label>
      {filters.map(filter => <label key={filter.label}>{filter.label}<select value={filter.value} onChange={event => { filter.set(event.target.value); setPage(1) }}><option value="">All {filter.label.toLowerCase()}s</option>{filter.items.map(item => <option key={item.id} value={item.id}>{item.name}</option>)}</select></label>)}
    </section>
    {error ? <div role="alert" className={styles.empty}>Catalog unavailable. <button type="button" onClick={() => setRefresh(value => value + 1)}>Retry</button></div> : !result ? <p aria-busy="true" className={styles.empty}>Loading catalog...</p> : <>
      <div className={styles.resultsBar}><span><strong>{result.totalItems}</strong> plugins <span className={styles.muted}>found</span></span><div className={styles.viewSwitch} aria-label="Catalog layout"><button type="button" aria-pressed={layout === 'table'} onClick={() => setLayout('table')}>Table</button><button type="button" aria-pressed={layout === 'cards'} onClick={() => setLayout('cards')}>Cards</button></div></div>
      {plugins.length === 0 ? <p className={styles.empty}>No plugins match these filters.</p> : layout === 'table' ? <div className={styles.tableWrap}><table><thead><tr><th>Plugin</th><th>Sources</th><th>Tags</th><th>Price</th><th>Folders</th><th>Last Update</th></tr></thead><tbody>{plugins.map(plugin => <PluginRow key={plugin.id} plugin={plugin} onOpen={() => navigate(`/plugins/catalog/${encodeURIComponent(plugin.id)}`)} />)}</tbody></table></div> : <div className={styles.cards}>{plugins.map(plugin => <PluginCard key={plugin.id} plugin={plugin} onOpen={() => navigate(`/plugins/catalog/${encodeURIComponent(plugin.id)}`)} />)}</div>}
      {result.totalItems > 0 && <nav className={styles.pagination} aria-label="Catalog pages"><span>Showing {(result.page - 1) * result.pageSize + 1}-{Math.min(result.page * result.pageSize, result.totalItems)} of {result.totalItems}</span><div><button type="button" disabled={result.page <= 1} onClick={() => setPage(result.page - 1)}>Previous</button><span>Page {result.page} of {result.totalPages}</span><button type="button" disabled={result.page >= result.totalPages} onClick={() => setPage(result.page + 1)}>Next</button></div></nav>}
    </>}
  </main>
}

