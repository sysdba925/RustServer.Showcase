import { useState } from 'react'
import ReactMarkdown from 'react-markdown'
import { isHttpUrl } from './importData'
import { uniqueSlug } from './library'
import { PluginIcon, Price, SourceList, TagList } from './PluginParts'
import type { LibraryPlugin, PrototypeLibrary } from './model'
import styles from './prototype.module.css'

type Props = { plugin: LibraryPlugin; library: PrototypeLibrary; isNew?: boolean; onSave: (plugin: LibraryPlugin, sites: string[]) => void; onBack: () => void }

export function PluginDetails({ plugin, library, isNew = false, onSave, onBack }: Props) {
  const [editing, setEditing] = useState(isNew)
  const [draft, setDraft] = useState(plugin)
  const [amount, setAmount] = useState(plugin.price?.amount.toString() ?? '')
  const [currency, setCurrency] = useState(plugin.price?.currency ?? 'USD')
  const [previews, setPreviews] = useState((plugin.previews ?? []).join('\n'))
  const [technical, setTechnical] = useState((plugin.technicalMedia ?? []).join('\n'))
  const [markdownPreview, setMarkdownPreview] = useState(false)
  const [error, setError] = useState('')
  const update = (patch: Partial<LibraryPlugin>) => setDraft({ ...draft, ...patch })
  const lines = (value: string) => value.split('\n').map(line => line.trim()).filter(Boolean)
  const save = () => {
    const name = draft.name.trim()
    if (!name) { setError('Name is required.'); return }
    if (library.plugins.some(item => item.id !== draft.id && item.name.trim().toLowerCase() === name.toLowerCase())) { setError('A plugin with this name already exists.'); return }
    const validMedia = (url: string) => isHttpUrl(url) || /^data:image\/(png|jpeg|gif|webp);base64,/i.test(url)
    if ((draft.iconUrl && !validMedia(draft.iconUrl)) || [...lines(previews), ...lines(technical)].some(url => !validMedia(url)) || draft.sources.some(source => !isHttpUrl(source.url))) { setError('Sources must use HTTP(S); media must use HTTP(S) or a supported image data URL.'); return }
    if (amount && (!Number.isFinite(Number(amount)) || Number(amount) < 0 || !/^[a-zA-Z]{3}$/.test(currency))) { setError('Enter a non-negative price and a three-letter currency.'); return }
    const sources = draft.sources.map(source => ({ url: source.url.trim(), site: library.sites.find(site => site.toLowerCase() === source.site.trim().toLowerCase()) ?? source.site.trim() }))
    const next = { ...draft, name, sources, slug: uniqueSlug(name, library.plugins, draft.id), price: amount ? { amount: Number(amount), currency: currency.toUpperCase() } : undefined, previews: lines(previews), technicalMedia: lines(technical) }
    onSave(next, sources.map(source => source.site).filter(Boolean)); setDraft(next); setEditing(false); setError('')
  }
  const toggle = (names: string[], name: string) => names.includes(name) ? names.filter(item => item !== name) : [...names, name]
  return <main className={styles.page}><div className={styles.detailTop}><button type="button" onClick={onBack}>← Back to catalog</button>{!editing && <button type="button" onClick={() => setEditing(true)}>Edit plugin</button>}</div>
    {editing ? <form className={styles.editor} onSubmit={event => { event.preventDefault(); save() }}><header><p className={styles.eyebrow}>{isNew ? 'New plugin' : 'Edit plugin'}</p><h1>{isNew ? 'Add plugin' : draft.name}</h1><p className={styles.muted}>Only the name is required. URLs are stored as links in this prototype.</p></header>
      <div className={styles.editorGrid}><label className={styles.field}>Name *<input required value={draft.name} onChange={event => update({ name: event.target.value })} /></label><label className={styles.field}>Author<input value={draft.author ?? ''} onChange={event => update({ author: event.target.value })} /></label><label className={styles.field}>Price<input type="number" min="0" step="any" value={amount} onChange={event => setAmount(event.target.value)} /></label><label className={styles.field}>Currency<input maxLength={3} value={currency} onChange={event => setCurrency(event.target.value)} /></label><label className={styles.field}>Last update by author<input type="date" value={draft.lastUpdate ?? ''} onChange={event => update({ lastUpdate: event.target.value })} /></label><label className={styles.field}>Icon URL<input type="text" value={draft.iconUrl ?? ''} onChange={event => update({ iconUrl: event.target.value })} placeholder="https://..." /></label></div>
      <label className={styles.field}>Description<textarea rows={3} value={draft.description ?? ''} onChange={event => update({ description: event.target.value })} /></label>
      <label className={styles.field}>Notes<textarea rows={3} value={draft.notes ?? ''} onChange={event => update({ notes: event.target.value })} /></label>
      <section><h2>Sources</h2>{draft.sources.map((source, index) => <div className={styles.sourceEditor} key={index}><label className={styles.field}>Site<input list="catalog-sites" value={source.site} onChange={event => update({ sources: draft.sources.map((item, i) => i === index ? { ...item, site: event.target.value } : item) })} /></label><label className={styles.field}>URL<input type="url" required value={source.url} onChange={event => update({ sources: draft.sources.map((item, i) => i === index ? { ...item, url: event.target.value } : item) })} /></label><button type="button" onClick={() => update({ sources: draft.sources.filter((_, i) => i !== index) })}>Remove</button></div>)}<datalist id="catalog-sites">{library.sites.map(site => <option key={site} value={site} />)}</datalist><button type="button" onClick={() => update({ sources: [...draft.sources, { site: '', url: '' }] })}>Add source</button></section>
      <div className={styles.organizerGrid}><fieldset><legend>Library statuses</legend>{library.tags.map(tag => <label key={tag.name}><input type="checkbox" checked={draft.tags.some(item => item.name === tag.name)} onChange={() => update({ tags: draft.tags.some(item => item.name === tag.name) ? draft.tags.filter(item => item.name !== tag.name) : [...draft.tags, tag] })} />{tag.name}</label>)}</fieldset><fieldset><legend>Folders</legend>{library.folders.map(folder => <label key={folder}><input type="checkbox" checked={draft.folders.includes(folder)} onChange={() => update({ folders: toggle(draft.folders, folder) })} />{folder}</label>)}</fieldset></div>
      <section><div className={styles.sectionHeading}><h2>Source description</h2><div className={styles.viewSwitch}><button type="button" aria-pressed={!markdownPreview} onClick={() => setMarkdownPreview(false)}>Edit Markdown</button><button type="button" aria-pressed={markdownPreview} onClick={() => setMarkdownPreview(true)}>Preview</button></div></div>{markdownPreview ? <div className={styles.markdown}><ReactMarkdown>{draft.sourceDescription || '*No source description.*'}</ReactMarkdown></div> : <label className={styles.field}><span className={styles.srOnly}>Source description Markdown</span><textarea rows={16} value={draft.sourceDescription ?? ''} onChange={event => update({ sourceDescription: event.target.value })} /></label>}</section>
      <section><h2>Media URLs</h2><p className={styles.muted}>One HTTP(S) URL per line; restored image data URLs are also supported. Icon is edited above. Media files are not uploaded.</p><div className={styles.editorGrid}><label className={styles.field}>Preview / screenshots<textarea rows={4} value={previews} onChange={event => setPreviews(event.target.value)} /></label><label className={styles.field}>Technical / Wiki<textarea rows={4} value={technical} onChange={event => setTechnical(event.target.value)} /></label></div></section>
      {error && <p role="alert" className={styles.error}>{error}</p>}<div className={styles.detailActions}><button type="submit">Save plugin</button><button type="button" onClick={() => { if (isNew) onBack(); else { setDraft(plugin); setAmount(plugin.price?.amount.toString() ?? ''); setCurrency(plugin.price?.currency ?? 'USD'); setPreviews((plugin.previews ?? []).join('\n')); setTechnical((plugin.technicalMedia ?? []).join('\n')); setEditing(false); setError('') } }}>Cancel</button></div>
    </form> : <><header className={styles.detailHeader}><PluginIcon plugin={plugin} /><div><p className={styles.eyebrow}>Plugin details</p><h1>{plugin.name}</h1><p>{plugin.author || 'Unknown author'}</p></div></header><div className={styles.detailLayout}><div className={styles.detailContent}><section><h2>Description</h2><p>{plugin.description || 'No description yet.'}</p></section><section><h2>Source description</h2><div className={styles.markdown}><ReactMarkdown>{plugin.sourceDescription || '*No source description yet.*'}</ReactMarkdown></div></section>{plugin.notes && <section><h2>Notes</h2><p>{plugin.notes}</p></section>}<section><h2>Media</h2><Media title="Icon" urls={plugin.iconUrl ? [plugin.iconUrl] : []} /><Media title="Preview" urls={plugin.previews ?? []} /><Media title="Technical / Wiki" urls={plugin.technicalMedia ?? []} /></section></div><aside className={styles.detailAside}><h2>Library</h2><h3>Sources</h3><SourceList sources={plugin.sources} /><h3>Price</h3><Price price={plugin.price} tags={plugin.tags} /><h3>Library statuses</h3><TagList tags={plugin.tags} /><h3>Folders</h3><p>{plugin.folders.join(', ') || '—'}</p><h3>Last update by author</h3><p>{plugin.lastUpdate || '—'}</p></aside></div></>}
  </main>
}

function Media({ title, urls }: { title: string; urls: string[] }) {
  return <div><h3>{title}</h3>{urls.length ? <div className={styles.mediaGrid}>{urls.map((url, index) => <a key={`${url}-${index}`} href={url} target="_blank" rel="noreferrer"><img src={url} alt={`${title} ${index + 1}`} /><span>{title} {index + 1} ↗</span></a>)}</div> : <p className={styles.muted}>No media.</p>}</div>
}
