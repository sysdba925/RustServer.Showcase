import { useRef, useState } from 'react'
import { HttpError, createPluginCatalogSite, updatePluginCatalogSite, deletePluginCatalogSite, createPluginCatalogTag, updatePluginCatalogTag, deletePluginCatalogTag, createPluginCatalogFolder, updatePluginCatalogFolder, deletePluginCatalogFolder, type SiteDto, type TagDto, type FolderDto } from '../../../shared/api'
import { Modal } from '../../catalog-prototype/Modal'
import styles from '../../catalog-prototype/prototype.module.css'
import managerStyles from './LibraryManager.module.css'

type Kind = 'folders' | 'tags' | 'sites'
type Item = SiteDto | TagDto | FolderDto
type Options = { sites: SiteDto[]; tags: TagDto[]; folders: FolderDto[] }
type Draft = { kind: Kind; id?: string; name: string; baseUrl: string; color: string }

export function LibraryManager({ initial, onClose, onChanged }: { initial: Options; onClose: () => void; onChanged: (deleted?: { kind: Kind; id: string }) => void }) {
  const [items, setItems] = useState(initial)
  const [draft, setDraft] = useState<Draft | null>(null)
  const [deleting, setDeleting] = useState<{ kind: Kind; item: Item } | null>(null)
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)
  const lock = useRef(false)

  async function run(action: () => Promise<void>) {
    if (lock.current) return
    lock.current = true; setBusy(true); setError('')
    try { await action() } catch (reason) {
      setError(reason instanceof HttpError ? reason.problemDetails?.detail || reason.problemDetails?.title || reason.message : reason instanceof Error ? reason.message : 'Operation failed.')
    } finally { lock.current = false; setBusy(false) }
  }

  function edit(kind: Kind, item?: Item) {
    setError(''); setDeleting(null)
    setDraft({ kind, id: item?.id, name: item?.name ?? '', baseUrl: item && 'baseUrl' in item ? item.baseUrl ?? '' : '', color: item && 'color' in item ? item.color : '#808080' })
  }

  function save() {
    if (!draft) return
    const name = draft.name.trim()
    if (!name || name.length > 200) { setError('Name is required and must be at most 200 characters.'); return }
    const baseUrl = draft.baseUrl.trim()
    if (draft.kind === 'sites' && baseUrl) {
      try { const url = new URL(baseUrl); if (!['http:', 'https:'].includes(url.protocol) || baseUrl.length > 2048) throw new Error() }
      catch { setError('Base URL must be an absolute HTTP(S) URL of at most 2048 characters.'); return }
    }
    if (draft.kind === 'tags' && !/^#[0-9a-fA-F]{6}$/.test(draft.color)) { setError('Color must use the #RRGGBB format.'); return }
    void run(async () => {
      let saved: Item
      if (draft.kind === 'sites') saved = draft.id ? await updatePluginCatalogSite(draft.id, { name, baseUrl: baseUrl || null }) : await createPluginCatalogSite({ name, baseUrl: baseUrl || null })
      else if (draft.kind === 'tags') saved = draft.id ? await updatePluginCatalogTag(draft.id, { name, color: draft.color }) : await createPluginCatalogTag({ name, color: draft.color })
      else saved = draft.id ? await updatePluginCatalogFolder(draft.id, { name }) : await createPluginCatalogFolder({ name })
      setItems(previous => ({ ...previous, [draft.kind]: [...previous[draft.kind].filter(item => item.id !== saved.id), saved].sort((a, b) => a.name.localeCompare(b.name)) }))
      setDraft(null); onChanged()
    })
  }

  function remove() {
    if (!deleting) return
    void run(async () => {
      const { kind, item } = deleting
      if (kind === 'sites') await deletePluginCatalogSite(item.id)
      else if (kind === 'tags') await deletePluginCatalogTag(item.id)
      else await deletePluginCatalogFolder(item.id)
      setItems(previous => ({ ...previous, [kind]: previous[kind].filter(entry => entry.id !== item.id) }))
      setDeleting(null); onChanged({ kind, id: item.id })
    })
  }

  return <Modal titleId="manage-title" onClose={() => { if (!lock.current) onClose() }}>
    <header className={styles.dialogHeader}><div><h2 id="manage-title">Manage library</h2><p>Manage catalog folders, tags and sites.</p></div><button type="button" disabled={busy} onClick={onClose}>Close</button></header>
    <div className={styles.managerGrid}>{(['folders', 'tags', 'sites'] as const).map(kind => <section key={kind} className={styles.manageSection}><h3>{kind}</h3>
      <button type="button" disabled={busy || !!draft || !!deleting} onClick={() => edit(kind)}>Add {kind.slice(0, -1)}</button>
      <ul className={styles.manageList}>{items[kind].map(item => <li key={item.id}><span>{'color' in item && <span className={managerStyles.swatch} style={{ backgroundColor: item.color }} />} {item.name}{'baseUrl' in item && item.baseUrl && <small className={managerStyles.url}>{item.baseUrl}</small>}</span><div><button type="button" aria-label={`Edit ${item.name}`} disabled={busy || !!draft || !!deleting} onClick={() => edit(kind, item)}>Edit<span className={styles.srOnly}> {item.name}</span></button><button type="button" aria-label={`Delete ${item.name}`} disabled={busy || !!draft || !!deleting} onClick={() => { setError(''); setDeleting({ kind, item }) }}>Delete<span className={styles.srOnly}> {item.name}</span></button></div></li>)}</ul>
    </section>)}</div>
    {draft && <form className={managerStyles.editor} noValidate onSubmit={event => { event.preventDefault(); save() }}><h3>{draft.id ? 'Edit' : 'Add'} {draft.kind.slice(0, -1)}</h3><label className={styles.field}>Name<input autoFocus maxLength={200} disabled={busy} value={draft.name} onChange={event => setDraft({ ...draft, name: event.target.value })} /></label>{draft.kind === 'sites' && <label className={styles.field}>Base URL (optional)<input type="url" maxLength={2048} disabled={busy} value={draft.baseUrl} onChange={event => setDraft({ ...draft, baseUrl: event.target.value })} /></label>}{draft.kind === 'tags' && <label className={styles.field}>Color (#RRGGBB)<input disabled={busy} value={draft.color} maxLength={7} onChange={event => setDraft({ ...draft, color: event.target.value })} /></label>}<div><button disabled={busy} type="submit">{busy ? 'Saving…' : 'Save'}</button> <button disabled={busy} type="button" onClick={() => { setDraft(null); setError('') }}>Cancel</button></div></form>}
    {deleting && <div className={managerStyles.editor}><p>Delete “{deleting.item.name}”? {deleting.kind === 'sites' ? 'Sources will be preserved without a site.' : 'Assignments will be removed; plugins will be preserved.'}</p><button type="button" disabled={busy} onClick={remove}>{busy ? 'Deleting…' : 'Confirm delete'}</button> <button type="button" disabled={busy} onClick={() => { setDeleting(null); setError('') }}>Cancel</button></div>}
    {error && <p role="alert" className={`${styles.error} ${managerStyles.error}`}>{error}</p>}
  </Modal>
}

