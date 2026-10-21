import { useRef, useState } from 'react'
import { commitPluginCatalogImport, previewPluginCatalogImport } from '../../../shared/api/plugin-catalog-client'
import { HttpError } from '../../../shared/api/request-json'
import type { ImportPreview, ImportResult } from '../../../shared/api/plugin-catalog-types'
import { Modal } from '../../catalog-prototype/Modal'
import styles from '../../catalog-prototype/prototype.module.css'

const example = { schemaVersion: 2, plugins: [{ name: 'Example Shop Plugin', sources: [{ site: 'Example Site', url: 'https://example.com/plugins/shop' }], author: 'Example Author', currentKnownVersion: '1.2.3', lastPublishedDate: '2026-10-18', description: 'Example description.', price: { amount: 14.99, currency: 'USD' }, tags: ['Nice to have'], folders: ['Shop'] }, { name: 'Example Minimal Plugin' }] }

export function ImportDialog({ onClose, onImported }: { onClose: () => void; onImported: () => void }) {
  const [document, setDocument] = useState<Record<string, unknown> | null>(null)
  const [preview, setPreview] = useState<ImportPreview | null>(null)
  const [result, setResult] = useState<ImportResult | null>(null)
  const [error, setError] = useState('')
  const [acceptNew, setAcceptNew] = useState(false)
  const [busy, setBusy] = useState(false)
  const [locked, setLocked] = useState(false)
  const pending = useRef(false)
  const unknown = { sites: [...new Set(preview?.entries.flatMap(row => row.unknownSites) ?? [])], tags: [...new Set(preview?.entries.flatMap(row => row.unknownTags) ?? [])], folders: [...new Set(preview?.entries.flatMap(row => row.unknownFolders) ?? [])] }
  const hasUnknown = Object.values(unknown).some(items => items.length > 0)
  const valid = !!preview && preview.entries.length > 0 && !preview.errors.length && preview.entries.every(row => !row.errors.length)
  const count = preview?.entries.filter(row => !row.duplicates.length).length ?? 0
  const download = () => { const url = URL.createObjectURL(new Blob([JSON.stringify(example, null, 2)], { type: 'application/json' })); const link = window.document.createElement('a'); link.href = url; link.download = 'plugin-catalog-example.json'; link.click(); URL.revokeObjectURL(url) }
  const choose = async (file: File | undefined) => {
    setDocument(null); setPreview(null); setResult(null); setError(''); setAcceptNew(false)
    if (!file) return
    setBusy(true); pending.current = true
    try {
      const parsed: unknown = JSON.parse(await file.text())
      if (!parsed || typeof parsed !== 'object' || Array.isArray(parsed)) throw new Error('The JSON document must be an object.')
      const data = { ...parsed, createMissing: false }
      const response = await previewPluginCatalogImport(data)
      setDocument(data); setPreview(response)
    } catch (err) { setError(err instanceof SyntaxError ? 'Invalid JSON file.' : err instanceof Error ? err.message : 'Could not preview the file.') }
    finally { pending.current = false; setBusy(false) }
  }
  const commit = async () => {
    if (pending.current || !document || !valid || (hasUnknown && !acceptNew)) return
    pending.current = true; setBusy(true); setError('')
    try {
      const response = await commitPluginCatalogImport({ ...document, createMissing: hasUnknown && acceptNew })
      setPreview(response.preview)
      if (response.committed) { setResult(response); setLocked(true); onImported() }
      else { setAcceptNew(false); setError('Import was not committed. Review the updated preview.') }
    } catch (err) {
      if (err instanceof HttpError && err.status === 400 && err.responseBody && typeof err.responseBody === 'object' && 'preview' in err.responseBody) {
        const response = err.responseBody as ImportResult
        setPreview(response.preview); setAcceptNew(false); setError('Import was rejected. Review the updated preview.')
      } else { setLocked(true); setError('Import failed or its outcome is unknown. Check the catalog before trying again.') }
    } finally { pending.current = false; setBusy(false) }
  }
  return <Modal titleId="import-title" onClose={() => { if (!busy) onClose() }}><header className={styles.dialogHeader}><div><h2 id="import-title">Import JSON</h2><p>Review one collection before adding it to the catalog.</p></div><button type="button" disabled={busy} onClick={onClose}>Close</button></header><div className={styles.importBody}>
    <p><strong>Required:</strong> schemaVersion (2), plugins (non-empty array), plugin name. <strong>Optional:</strong> sources (url, site, label), author, description, sourceDescription (Markdown), notes, price (amount, currency), currentKnownVersion, lastPublishedDate (YYYY-MM-DD), tags, folders. No IDs, slugs or media files.</p>
    <button type="button" onClick={download}>Download example JSON</button>
    <label className={styles.field}>Choose JSON file<input type="file" accept=".json,application/json" disabled={busy || locked} onChange={event => void choose(event.target.files?.[0])} /></label>
    {busy && <p role="status">Working...</p>}{error && <p className={styles.error} role="alert">{error}</p>}{result && <p role="status">Created: {result.created}; Skipped: {result.skipped}.</p>}
    {preview && <><h3>Preview · {preview.entries.length} records</h3><p>Matching names or source URLs are skipped; existing records are never overwritten.</p>{preview.errors.map((message, index) => <p className={styles.error} key={index}>{message}</p>)}<div className={styles.importRows}>{preview.entries.map(row => <details key={row.index} open={!!row.errors.length || !!row.duplicates.length}><summary>#{Number(row.index) + 1} · {row.name || 'Invalid record'}{row.errors.length ? ' · Invalid' : row.duplicates.length ? ' · Duplicate' : ' · Ready'}</summary>{row.errors.map((message, index) => <p className={styles.error} key={index}>{message}</p>)}{row.duplicates.length > 0 && <p>Matches: {row.duplicates.join(', ')}</p>}{row.input && <pre>{JSON.stringify(row.input, null, 2)}</pre>}</details>)}</div>
      {hasUnknown && <div><p>New sites: {unknown.sites.join(', ') || '—'}<br />New tags: {unknown.tags.join(', ') || '—'}<br />New folders: {unknown.folders.join(', ') || '—'}</p><label><input type="checkbox" checked={acceptNew} disabled={busy || locked} onChange={event => setAcceptNew(event.target.checked)} /> Create these missing library items</label></div>}
      {!valid && <p className={styles.error}>Fix the errors in the file before importing.</p>}
      <button type="button" disabled={busy || locked || !valid || (hasUnknown && !acceptNew)} onClick={() => void commit()}>Confirm import ({count})</button>
    </>}
  </div></Modal>
}
