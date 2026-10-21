import { useRef, useState } from 'react'
import { externalPluginMediaKinds, HttpError, localPluginMediaKinds, resolvePluginCatalogContentUrl, type ExternalPluginMediaKind, type PluginDetailsDto, type PluginMediaDto } from '../../../shared/api'
import { PluginMediaGallery } from './PluginMediaGallery'
import type { PluginCatalogApi } from './api'
import styles from './PluginDetailsPage.module.css'

type Dialog = { mode: 'upload' | 'external' } | { mode: 'edit' | 'delete' | 'preview'; media: PluginMediaDto }

export function CatalogMedia({ plugin, api, onChange, onBusyChange }: { plugin: PluginDetailsDto; api: PluginCatalogApi; onChange: (patch: Partial<PluginDetailsDto>) => void; onBusyChange: (busy: boolean) => void }) {
  const [dialog, setDialog] = useState<Dialog | null>(null)
  const [kind, setKind] = useState<string>('Icon')
  const [file, setFile] = useState<File | null>(null)
  const [url, setUrl] = useState('')
  const [caption, setCaption] = useState('')
  const [order, setOrder] = useState('0')
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)
  const lock = useRef(false)

  function open(next: Dialog) {
    setError(''); setFile(null); setUrl('')
    setKind(next.mode === 'external' ? 'Video' : 'Icon')
    setCaption('media' in next ? next.media.caption ?? '' : '')
    setOrder('media' in next ? String(next.media.sortOrder) : '0')
    setDialog(next)
  }
  async function run(action: () => Promise<unknown>) {
    if (lock.current) return
    lock.current = true; setBusy(true); onBusyChange(true); setError('')
    try {
      await action()
      setDialog(null)
      try { const refreshed = await api.getPlugin(plugin.id); onChange({ media: refreshed.media, icon: refreshed.icon, iconMediaId: refreshed.iconMediaId, updatedAt: refreshed.updatedAt }) }
      catch { setError('Media was saved, but details could not be refreshed. Reload the page to see the latest data.') }
    } catch (reason) {
      setError(reason instanceof HttpError ? reason.problemDetails?.detail || reason.problemDetails?.title || reason.message : reason instanceof Error ? reason.message : 'Could not save media.')
    } finally { lock.current = false; setBusy(false); onBusyChange(false) }
  }
  function save() {
    if (!dialog || !['upload', 'external', 'edit'].includes(dialog.mode)) return
    const number = Number(order)
    if (!/^\d+$/.test(order) || !Number.isSafeInteger(number) || number > 2147483647) { setError('Sort order must be a non-negative integer.'); return }
    if (caption.length > 500) { setError('Caption must be at most 500 characters.'); return }
    const metadata = { caption: caption.trim() || null, sortOrder: number }
    if (dialog.mode === 'upload') {
      if (!file || !['image/png', 'image/jpeg', 'image/webp', 'image/gif'].includes(file.type)) { setError('Choose a PNG, JPEG, WebP or GIF image.'); return }
      if (file.size > 10 * 1024 * 1024) { setError('Image must be at most 10 MiB.'); return }
      const form = new FormData(); form.append('file', file); form.append('kind', kind); form.append('sortOrder', order); if (metadata.caption) form.append('caption', metadata.caption)
      void run(() => api.uploadMedia(plugin.id, form))
    } else if (dialog.mode === 'external') {
      const externalUrl = url.trim()
      try { const parsed = new URL(externalUrl); if (!['http:', 'https:'].includes(parsed.protocol)) throw new Error() } catch { setError('Enter an absolute HTTP(S) URL.'); return }
      void run(() => api.createExternalMedia(plugin.id, { kind: kind as ExternalPluginMediaKind, externalUrl, ...metadata }))
    } else if (dialog.mode === 'edit') void run(() => api.updateMedia(plugin.id, dialog.media.id, metadata))
  }
  const editing = dialog && ['upload', 'external', 'edit'].includes(dialog.mode)
  return <>
    <PluginMediaGallery media={plugin.media} iconMediaId={plugin.iconMediaId} pending={busy || !!dialog} onUpload={() => open({ mode: 'upload' })} onAddExternal={() => open({ mode: 'external' })} onEdit={media => open({ mode: 'edit', media })} onDelete={media => open({ mode: 'delete', media })} onPreview={media => open({ mode: 'preview', media })} onSetIcon={mediaId => void run(() => api.updateIcon(plugin.id, { mediaId }))} onClearIcon={() => void run(() => api.updateIcon(plugin.id, { mediaId: null }))} />
    {error && !dialog && <p className={styles.formError} role="alert">{error}</p>}
    {dialog && <div className={styles.modalVeil}><div className={styles.modal} role="dialog" aria-modal="true" aria-labelledby="media-dialog-title">
      <div className={styles.modalHeader}><h2 id="media-dialog-title">{dialog.mode === 'upload' ? 'Upload image' : dialog.mode === 'external' ? 'Add external media' : dialog.mode === 'edit' ? 'Edit media' : dialog.mode === 'delete' ? 'Delete media' : 'Original image'}</h2></div>
      {editing && <form noValidate onSubmit={event => { event.preventDefault(); save() }}><div className={styles.modalBody}>
        {(dialog.mode === 'upload' || dialog.mode === 'external') && <label className={styles.field}>Kind<select value={kind} disabled={busy} onChange={event => setKind(event.target.value)}>{(dialog.mode === 'upload' ? localPluginMediaKinds : externalPluginMediaKinds).map(value => <option key={value} value={value}>{value}</option>)}</select></label>}
        {dialog.mode === 'upload' && <label className={styles.field}>Image<input type="file" accept="image/png,image/jpeg,image/webp,image/gif" disabled={busy} onChange={event => setFile(event.target.files?.[0] ?? null)} /><small>PNG, JPEG, WebP or GIF, up to 10 MiB.</small></label>}
        {dialog.mode === 'external' && <label className={styles.field}>URL<input type="url" value={url} disabled={busy} onChange={event => setUrl(event.target.value)} /></label>}
        <label className={styles.field}>Caption<input maxLength={500} value={caption} disabled={busy} onChange={event => setCaption(event.target.value)} /></label>
        <label className={styles.field}>Sort order<input type="number" min="0" step="1" value={order} disabled={busy} onChange={event => setOrder(event.target.value)} /></label>
        {error && <p className={styles.formError} role="alert">{error}</p>}</div><div className={styles.modalFooter}><button type="button" disabled={busy} onClick={() => setDialog(null)}>Cancel</button><button className={styles.primaryButton} type="submit" disabled={busy}>{busy ? 'Saving…' : 'Save'}</button></div></form>}
      {dialog.mode === 'delete' && <><div className={styles.modalBody}><p>Delete “{dialog.media.caption || dialog.media.kind}”?{dialog.media.id === plugin.iconMediaId && ' The selected icon will also be cleared.'}</p>{error && <p className={styles.formError} role="alert">{error}</p>}</div><div className={styles.modalFooter}><button type="button" disabled={busy} onClick={() => setDialog(null)}>Cancel</button><button className={styles.dangerButton} type="button" disabled={busy} onClick={() => void run(() => api.deleteMedia(plugin.id, dialog.media.id))}>{busy ? 'Deleting…' : 'Confirm delete'}</button></div></>}
      {dialog.mode === 'preview' && <><div className={styles.previewBody}><img className={styles.originalImage} src={resolvePluginCatalogContentUrl(dialog.media.accessUrl)} alt={dialog.media.caption ?? dialog.media.kind} /></div><div className={styles.modalFooter}><button type="button" onClick={() => setDialog(null)}>Close</button></div></>}
    </div></div>}
  </>
}

