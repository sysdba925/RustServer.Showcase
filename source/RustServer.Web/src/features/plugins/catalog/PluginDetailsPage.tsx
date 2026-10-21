import { useEffect, useState } from 'react'
import ReactMarkdown from 'react-markdown'
import { Link, useNavigate, useParams } from 'react-router-dom'

import { getPluginCatalogSites, HttpError, resolvePluginCatalogContentUrl, type PluginDetailsDto, type SiteDto } from '../../../shared/api'
import { PluginIcon, Price, TagList } from '../../catalog-prototype/PluginParts'
import type { LibraryPlugin } from '../../catalog-prototype/model'
import styles from '../../catalog-prototype/prototype.module.css'
import detailStyles from './CatalogDetails.module.css'
import { pluginCatalogApi, type PluginCatalogApi } from './api'
import { PluginEditor } from './PluginEditor'
import { CatalogMedia } from './CatalogMedia'
import { Modal } from '../../catalog-prototype/Modal'

interface Props {
  api?: Pick<PluginCatalogApi, 'getPlugin'> & Partial<Pick<PluginCatalogApi, 'updatePlugin' | 'uploadMedia' | 'createExternalMedia' | 'updateMedia' | 'deleteMedia' | 'updateIcon' | 'deletePlugin'>>
  getSites?: typeof getPluginCatalogSites
}

type State =
  | { status: 'loading' }
  | { status: 'not-found' }
  | { status: 'error' }
  | { status: 'ready'; plugin: PluginDetailsDto; sites: SiteDto[] }

function formatDate(value: string | null): string {
  if (!value) return '—'
  const date = new Date(value)
  return Number.isNaN(date.valueOf()) ? value : new Intl.DateTimeFormat(undefined, { dateStyle: 'medium', timeStyle: 'short' }).format(date)
}

export function PluginDetailsPage({ api = pluginCatalogApi, getSites = getPluginCatalogSites }: Props) {
  const navigate = useNavigate()
  const { pluginId } = useParams<{ pluginId: string }>()
  const [state, setState] = useState<State>({ status: 'loading' })
  const [refresh, setRefresh] = useState(0)
  const [editing, setEditing] = useState(false)
  const [organizing, setOrganizing] = useState(false)
  const [deleting, setDeleting] = useState(false)
  const [deleteBusy, setDeleteBusy] = useState(false)
  const [deleteError, setDeleteError] = useState('')

  useEffect(() => {
    const controller = new AbortController()
    // oxlint-disable-next-line react/set-state-in-effect -- hide data from the previous request
    setState({ status: 'loading' })
    if (!pluginId) {
      setState({ status: 'not-found' })
      return () => controller.abort()
    }
    void api.getPlugin(pluginId, controller.signal).then(plugin => {
      return getSites(controller.signal).then(sites => {
        if (!controller.signal.aborted) setState({ status: 'ready', plugin, sites })
      }).catch(() => {
        if (!controller.signal.aborted) setState({ status: 'error' })
      })
    }).catch((error: unknown) => {
      if (!controller.signal.aborted) setState({ status: error instanceof HttpError && error.status === 404 ? 'not-found' : 'error' })
    })
    return () => controller.abort()
  }, [api, getSites, pluginId, refresh])

  return <main className={styles.page}>
    <div className={styles.detailTop}><Link className={detailStyles.back} to="/plugins/catalog">← Back to catalog</Link>{state.status === 'ready' && !editing && <div><button type="button" disabled={organizing} onClick={() => setEditing(true)}>Edit plugin</button> <button className={detailStyles.deleteButton} type="button" disabled={organizing} onClick={() => { setDeleteError(''); setDeleting(true) }}>Delete</button></div>}</div>
    {state.status === 'loading' && <p className={styles.empty} aria-busy="true">Loading plugin details...</p>}
    {state.status === 'not-found' && <section className={styles.empty}><h1>Plugin not found</h1><p>This catalog entry does not exist or is no longer available.</p></section>}
    {state.status === 'error' && <section className={styles.empty} role="alert"><h1>Plugin details unavailable</h1><p>Could not load catalog details.</p><button type="button" onClick={() => setRefresh(value => value + 1)}>Retry</button></section>}
    {state.status === 'ready' && (editing ? <PluginEditor plugin={state.plugin} api={{ ...pluginCatalogApi, ...api }} onCancel={() => { setEditing(false); setRefresh(value => value + 1) }} onComplete={plugin => { setState({ ...state, plugin }); setEditing(false) }} /> : <Details key={state.plugin.id} plugin={state.plugin} sites={state.sites} api={{ ...pluginCatalogApi, ...api }} onBusyChange={setOrganizing} onChange={patch => setState(current => current.status === 'ready' ? { ...current, plugin: { ...current.plugin, ...patch } } : current)} />)}
    {deleting && state.status === 'ready' && <Modal titleId="delete-plugin-title" onClose={() => { if (!deleteBusy) setDeleting(false) }}><div className={styles.importBody}><h2 id="delete-plugin-title">Delete {state.plugin.name}?</h2><p>This permanently deletes the catalog entry, {state.plugin.sources.length} source(s), and {state.plugin.media.length} media item(s). Rust server plugin files are not affected.</p>{deleteError && <p role="alert" className={styles.error}>{deleteError}</p>}<button type="button" disabled={deleteBusy} onClick={() => setDeleting(false)}>Cancel</button> <button type="button" disabled={deleteBusy} onClick={() => { setDeleteBusy(true); setDeleteError(''); void (api.deletePlugin ?? pluginCatalogApi.deletePlugin)(state.plugin.id).then(() => navigate('/plugins/catalog')).catch((error: unknown) => setDeleteError(error instanceof HttpError ? error.problemDetails?.detail || error.message : 'Deletion failed. Please try again.')).finally(() => setDeleteBusy(false)) }}>{deleteBusy ? 'Deleting…' : 'Confirm delete'}</button></div></Modal>}
  </main>
}

function Details({ plugin, sites, api, onChange, onBusyChange }: { plugin: PluginDetailsDto; sites: SiteDto[]; api: PluginCatalogApi; onChange: (patch: Partial<PluginDetailsDto>) => void; onBusyChange: (busy: boolean) => void }) {
  const icon: LibraryPlugin = {
    id: plugin.id, name: plugin.name, slug: plugin.slug, sources: [], tags: [], folders: [],
    iconUrl: plugin.icon ? resolvePluginCatalogContentUrl(plugin.icon.thumbnailAccessUrl) : undefined,
  }
  const price = plugin.price !== null && plugin.currency !== null ? { amount: Number(plugin.price), currency: plugin.currency } : undefined
  return <>
    <header className={styles.detailHeader}><PluginIcon plugin={icon} /><div><p className={styles.eyebrow}>Plugin details</p><h1>{plugin.name}</h1><p>{plugin.author || 'Unknown author'}</p></div></header>
    <div className={styles.detailLayout}>
      <div className={styles.detailContent}>
        <section><h2>Description</h2><p className={detailStyles.text}>{plugin.description || 'No description yet.'}</p></section>
        <section><h2>Source description</h2><div className={styles.markdown}><ReactMarkdown>{plugin.sourceDescription || '*No source description yet.*'}</ReactMarkdown></div></section>
        <section><h2>Notes</h2><p className={detailStyles.text}>{plugin.notes || 'No notes.'}</p></section>
        <CatalogMedia plugin={plugin} api={api} onChange={onChange} onBusyChange={onBusyChange} />
      </div>
      <aside className={styles.detailAside}>
        <h2>Library</h2>
        <h3>Sources</h3>{plugin.sources.length ? plugin.sources.map(source => <p key={source.id}><a href={source.url} target="_blank" rel="noreferrer">{source.label || source.url}</a>{source.siteId && ` · ${sites.find(site => site.id === source.siteId)?.name ?? 'Unknown site'}`}</p>) : <p>—</p>}
        <h3>Price</h3><Price price={price} tags={plugin.tags} />
        <h3>Tags</h3><TagList tags={plugin.tags} />
        <h3>Folders</h3><p>{plugin.folders.map(folder => folder.name).join(', ') || '—'}</p>
        <h3>Catalog-known version</h3><p>{plugin.currentKnownVersion || '—'}</p>
        <h3>Last published date</h3><p>{plugin.lastPublishedDate || '—'}</p>
        <h3>Catalog slug</h3><p>{plugin.slug}</p>
        <h3>Created in catalog</h3><p>{formatDate(plugin.createdAt)}</p>
        <h3>Updated in catalog</h3><p>{formatDate(plugin.updatedAt)}</p>
      </aside>
    </div>
  </>
}
