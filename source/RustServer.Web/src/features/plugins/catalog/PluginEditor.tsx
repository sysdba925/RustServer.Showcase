import { useEffect, useRef, useState } from 'react'
import ReactMarkdown from 'react-markdown'
import { getPluginCatalogFolders, getPluginCatalogSites, getPluginCatalogTags, HttpError, resolvePluginCatalogContentUrl, type FolderDto, type PluginDetailsDto, type SiteDto, type TagDto } from '../../../shared/api'
import { pluginCatalogApi, type PluginCatalogApi } from './api'
import styles from '../../catalog-prototype/prototype.module.css'
import form from './PluginEditor.module.css'

type Source = { url: string; siteId: string | null; label: string | null }
type Options = { sites: SiteDto[]; tags: TagDto[]; folders: FolderDto[] }

export function PluginEditor({ plugin, api = pluginCatalogApi, onCancel, onComplete }: { plugin?: PluginDetailsDto; api?: PluginCatalogApi; onCancel: () => void; onComplete: (plugin: PluginDetailsDto) => void }) {
  const [saved, setSaved] = useState(plugin)
  const [name, setName] = useState(plugin?.name ?? '')
  const [author, setAuthor] = useState(plugin?.author ?? '')
  const [description, setDescription] = useState(plugin?.description ?? '')
  const [notes, setNotes] = useState(plugin?.notes ?? '')
  const [markdown, setMarkdown] = useState(plugin?.sourceDescription ?? '')
  const [sources, setSources] = useState<Source[]>(plugin?.sources.map(({ url, siteId, label }) => ({ url, siteId, label })) ?? [{ url: '', siteId: null, label: null }])
  const [tags, setTags] = useState(plugin?.tags.map(x => x.id) ?? [])
  const [folders, setFolders] = useState(plugin?.folders.map(x => x.id) ?? [])
  const [options, setOptions] = useState<Options | null>(null)
  const initialVersion = plugin?.currentKnownVersion ?? ''
  const [legacy, setLegacy] = useState(initialVersion && !/^\d+\.\d+\.\d+$/.test(initialVersion) ? initialVersion : '')
  const [version, setVersion] = useState(initialVersion && !/^\d+\.\d+\.\d+$/.test(initialVersion) ? ['', '', ''] : initialVersion ? initialVersion.split('.') : ['', '', ''])
  const versionRefs = useRef<(HTMLInputElement | null)[]>([])
  const [price, setPrice] = useState(plugin?.price?.toString() ?? '')
  const [currency, setCurrency] = useState(plugin?.currency ?? '')
  const [date, setDate] = useState(plugin?.lastPublishedDate ?? '')
  const [icon, setIcon] = useState<File | null>(null)
  const [iconUrl, setIconUrl] = useState('')
  const [removeIcon, setRemoveIcon] = useState(false)
  const [uploadedIcon, setUploadedIcon] = useState<string | null>(null)
  const [preview, setPreview] = useState(false)
  const [expanded, setExpanded] = useState(false)
  const [error, setError] = useState('')
  const [saving, setSaving] = useState(false)
  const [createOutcomeUnknown, setCreateOutcomeUnknown] = useState(false)
  const lock = useRef(false)
  useEffect(() => { const controller = new AbortController(); void Promise.all([getPluginCatalogSites(controller.signal), getPluginCatalogTags(controller.signal), getPluginCatalogFolders(controller.signal)]).then(([sites, tags, folders]) => setOptions({ sites, tags, folders })).catch(() => { if (!controller.signal.aborted) setError('Could not load sites, tags and folders. Reload the page to try again.') }); return () => controller.abort() }, [])
  useEffect(() => { if (!icon) { setIconUrl(''); return }; const url = URL.createObjectURL(icon); setIconUrl(url); return () => URL.revokeObjectURL(url) }, [icon])
  function changeSource(index: number, patch: Partial<Source>) { setSources(items => items.map((item, i) => i === index ? { ...item, ...patch } : item)) }
  async function save() {
    if (lock.current || !options) return
    if (!name.trim()) { setError('Name is required.'); return }
    if (Boolean(price.trim()) !== Boolean(currency.trim()) || (price.trim() && (!/^(?:0|[1-9]\d{0,15})(?:\.\d{1,2})?$/.test(price.trim()) || !/^[A-Za-z]{3}$/.test(currency.trim())))) { setError('Enter a non-negative price with up to two decimals and a three-letter currency together.'); return }
    if (!legacy && version.join('.').length > 100) { setError('Version must be at most 100 characters.'); return }
    if (!legacy && version.some(Boolean) && !version.every(Boolean)) { setError('Enter all three version segments.'); return }
    if (sources.some(s => !s.url.trim() && (s.siteId || s.label))) { setError('A source with a site or label needs a URL.'); return }
    if (icon && (icon.size > 10 * 1024 * 1024 || !['image/png', 'image/jpeg', 'image/webp', 'image/gif'].includes(icon.type))) { setError('Choose a PNG, JPEG, WebP or GIF image up to 10 MiB.'); return }
    lock.current = true; setSaving(true); setError('')
    try {
      const request = { name: name.trim(), author: author || null, description: description || null, notes: notes || null, sourceDescription: markdown || null, price: price.trim() || null, currency: currency.trim().toUpperCase() || null, currentKnownVersion: legacy || (version.some(Boolean) ? version.join('.') : null), lastPublishedDate: date || null, sources: sources.filter(s => s.url.trim()).map(s => ({ ...s, url: s.url.trim(), label: s.label?.trim() || null })), tagIds: tags, folderIds: folders }
      if (!saved) setCreateOutcomeUnknown(true)
      let current = saved ? await api.updatePlugin(saved.id, request) : await api.createPlugin(request)
      setCreateOutcomeUnknown(false)
      setSaved(current)
      if (icon) {
        let mediaId = uploadedIcon
        if (!mediaId) { const data = new FormData(); data.append('file', icon); data.append('kind', 'Icon'); data.append('sortOrder', '0'); const media = await api.uploadMedia(current.id, data); mediaId = media.id; setUploadedIcon(mediaId) }
        current = await api.updateIcon(current.id, { mediaId })
      } else if (removeIcon && current.iconMediaId) current = await api.updateIcon(current.id, { mediaId: null })
      onComplete(current)
    } catch (reason) { const message = reason instanceof HttpError ? reason.problemDetails?.detail || reason.problemDetails?.title || reason.message : reason instanceof Error ? reason.message : 'Could not save plugin.'; setError(`${message} ${!saved && !plugin ? 'If creation failed, check the catalog before trying again. If the card was saved, retry will update it.' : 'The card may already be saved; retry to finish.'}`) }
    finally { lock.current = false; setSaving(false) }
  }
  const selectedIcon = iconUrl || (!removeIcon && saved?.icon ? resolvePluginCatalogContentUrl(saved.icon.thumbnailAccessUrl) : '')
  return <form className={`${styles.editor} ${form.editor}`} onSubmit={e => { e.preventDefault(); void save() }}>
    <header className={form.toolbar}><h1>{plugin ? 'Edit plugin' : 'Add plugin'}</h1><div><button type="button" disabled={saving} onClick={() => saved && !plugin ? onComplete(saved) : onCancel()}>Cancel</button> <button type="submit" disabled={saving || !options || (createOutcomeUnknown && !saved)}>{saving ? 'Saving…' : plugin ? 'Save changes' : 'Create plugin'}</button></div></header>
    {error && <p role="alert" className={styles.error}>{error}</p>}
    <div className={form.columns}><div className={form.column}>
      <div className={form.pair}><label className={styles.field}>Name *<input required maxLength={200} value={name} onChange={e => setName(e.target.value)} /></label><label className={styles.field}>Author<input maxLength={200} value={author} onChange={e => setAuthor(e.target.value)} /></label></div>
      <section><div className={form.sectionTitle}><strong>Sources</strong><button type="button" onClick={() => setSources([...sources, { url: '', siteId: null, label: null }])}>Add source</button></div><div className={form.sources}>{sources.map((s, i) => <div className={form.source} key={i}><label className={styles.field}>Plugin URL<input type="url" maxLength={2048} value={s.url} onChange={e => changeSource(i, { url: e.target.value })} /></label><label className={styles.field}>Site<select value={s.siteId ?? ''} onChange={e => changeSource(i, { siteId: e.target.value || null })}><option value="">None</option>{options?.sites.map(site => <option key={site.id} value={site.id}>{site.name}</option>)}</select></label><label className={styles.field}>Source label<input maxLength={200} value={s.label ?? ''} onChange={e => changeSource(i, { label: e.target.value || null })} /></label><button type="button" aria-label={`Remove source ${i + 1}`} onClick={() => setSources(sources.filter((_, n) => n !== i))}>×</button></div>)}</div></section>
      <label className={styles.field}>Description<textarea maxLength={8000} rows={2} value={description} onChange={e => setDescription(e.target.value)} /></label>
      <label className={styles.field}>Notes<textarea rows={2} value={notes} onChange={e => setNotes(e.target.value)} /></label>
      <section><div className={form.sectionTitle}><strong>Source description</strong><div className={styles.viewSwitch}><button type="button" aria-pressed={!preview} onClick={() => setPreview(false)}>Edit Markdown</button><button type="button" aria-pressed={preview} onClick={() => setPreview(true)}>Preview</button><button type="button" onClick={() => setExpanded(!expanded)}>{expanded ? 'Collapse' : 'Expand'}</button></div></div>{preview ? <div className={`${styles.markdown} ${form.markdown} ${expanded ? form.expanded : ''}`}><ReactMarkdown>{markdown || '*No source description.*'}</ReactMarkdown></div> : <label className={styles.field}><span className={styles.srOnly}>Source description Markdown</span><textarea rows={expanded ? 16 : 3} value={markdown} onChange={e => setMarkdown(e.target.value)} /></label>}</section>
    </div><div className={form.column}>
      <div><strong>Icon</strong><div className={form.icon}>{selectedIcon && <img src={selectedIcon} alt="Selected icon" />}<label className={styles.field}>Select image<input type="file" accept="image/png,image/jpeg,image/webp,image/gif" onChange={e => { setIcon(e.target.files?.[0] ?? null); setUploadedIcon(null); setRemoveIcon(false) }} /></label>{(selectedIcon || icon) && <button type="button" onClick={() => { setIcon(null); setUploadedIcon(null); setRemoveIcon(true) }}>Remove</button>}</div></div>
      <div className={styles.field}>Known version{legacy ? <div>Existing value: {legacy} <button type="button" onClick={() => { setLegacy(''); setVersion(['', '', '']) }}>Change version</button></div> : <div className={form.version}>{version.map((value, i) => <span key={i}>{i > 0 && <b>.</b>}<input aria-label={['Major', 'Minor', 'Patch'][i]} inputMode="numeric" ref={el => { versionRefs.current[i] = el }} value={value} onChange={e => setVersion(v => v.map((part, n) => n === i ? e.target.value.replace(/\D/g, '') : part))} onKeyDown={e => { if ((e.key === '.' || e.key === ',' || e.key === 'ArrowRight') && i < 2) { e.preventDefault(); versionRefs.current[i + 1]?.focus() } else if ((e.key === 'ArrowLeft' || (e.key === 'Backspace' && !value)) && i > 0) { e.preventDefault(); versionRefs.current[i - 1]?.focus() } }} onPaste={e => { const parts = e.clipboardData.getData('text').split(/[.,]/); if (parts.length > 1 && parts.every(p => /^\d+$/.test(p)) && parts.length <= 3 - i) { e.preventDefault(); setVersion(v => v.map((part, n) => n >= i && n < i + parts.length ? parts[n - i] : part)); versionRefs.current[Math.min(i + parts.length - 1, 2)]?.focus() } }} /></span>)}</div>}</div>
      <div className={form.pair}><label className={styles.field}>Price<input inputMode="decimal" value={price} onChange={e => setPrice(e.target.value)} /></label><label className={styles.field}>Currency<select value={currency} onChange={e => setCurrency(e.target.value)}><option value="">None</option>{Array.from(new Set([...Intl.supportedValuesOf('currency'), ...(currency ? [currency] : [])])).sort().map(code => <option key={code} value={code}>{code}</option>)}</select></label></div>
      <Calendar value={date} onChange={setDate} />
      <MultiSelect label="Tags" options={options?.tags ?? []} value={tags} onChange={setTags} />
      <MultiSelect label="Folders" options={options?.folders ?? []} value={folders} onChange={setFolders} />
    </div></div>
  </form>
}
function MultiSelect({ label, options, value, onChange }: { label: string; options: { id: string; name: string }[]; value: string[]; onChange: (v: string[]) => void }) { return <fieldset className={form.multiselect}><legend>{label}</legend><div>{options.length ? options.map(item => <label key={item.id}><input type="checkbox" checked={value.includes(item.id)} onChange={e => onChange(e.target.checked ? [...value, item.id] : value.filter(id => id !== item.id))} /> {item.name}</label>) : 'No options'}</div></fieldset> }
function Calendar({ value, onChange }: { value: string; onChange: (v: string) => void }) {
  const now = new Date(); const [year, setYear] = useState(value ? Number(value.slice(0, 4)) : now.getFullYear()); const [month, setMonth] = useState(value ? Number(value.slice(5, 7)) : now.getMonth() + 1)
  const days = new Date(year, month, 0).getDate(); const pad = (n: number) => String(n).padStart(2, '0')
  return <details className={form.calendar}><summary>Last published date: {value || '—'}</summary><div><label>Year <select value={year} onChange={e => setYear(Number(e.target.value))}>{Array.from({ length: 201 }, (_, i) => now.getFullYear() + 20 - i).concat(year < now.getFullYear() - 180 || year > now.getFullYear() + 20 ? [year] : []).map(y => <option key={y} value={y}>{y}</option>)}</select></label> <label>Month <select value={month} onChange={e => setMonth(Number(e.target.value))}>{Array.from({ length: 12 }, (_, i) => <option key={i} value={i + 1}>{new Intl.DateTimeFormat(undefined, { month: 'long' }).format(new Date(2000, i, 1))}</option>)}</select></label><div className={form.days}>{Array.from({ length: days }, (_, i) => <button type="button" key={i} aria-pressed={value === `${year}-${pad(month)}-${pad(i + 1)}`} onClick={() => onChange(`${year}-${pad(month)}-${pad(i + 1)}`)}>{i + 1}</button>)}</div><button type="button" onClick={() => { setYear(now.getFullYear()); setMonth(now.getMonth() + 1); onChange(`${now.getFullYear()}-${pad(now.getMonth() + 1)}-${pad(now.getDate())}`) }}>Today</button> <button type="button" onClick={() => onChange('')}>Clear</button></div></details>
}
