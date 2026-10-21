import { useRef, useState } from 'react'
import { commitPluginCatalogRestore, previewPluginCatalogRestore } from '../../../shared/api/plugin-catalog-client'
import type { RestorePreview } from '../../../shared/api/plugin-catalog-types'
import { HttpError } from '../../../shared/api/request-json'
import { Modal } from '../../catalog-prototype/Modal'
import styles from '../../catalog-prototype/prototype.module.css'

const lockKey = 'catalog-restore-outcome-unknown'
const maxSize = 64 * 1024 * 1024
const labels = ['plugins', 'sites', 'tags', 'folders', 'sources', 'media'] as const
function message(error: unknown): string {
  if (error instanceof HttpError) {
    switch (error.status) {
      case 400: return 'The archive is invalid or exceeds an internal archive limit.'
      case 409: return 'Restore was rejected: the catalog is not empty or media already exists.'
      case 413: return 'The archive exceeds the server request size limit.'
      case 415: return 'The server rejected the archive content type.'
      default: return `Catalog service error (HTTP ${error.status}).`
    }
  }
  return 'Could not contact the catalog service.'
}

export function RestoreDialog({ onClose, onRestored }: { onClose: () => void; onRestored: () => void }) {
  const [file, setFile] = useState<File | null>(null)
  const [preview, setPreview] = useState<RestorePreview | null>(null)
  const [confirmed, setConfirmed] = useState(false)
  const [busy, setBusy] = useState(false)
  const [locked, setLocked] = useState(() => localStorage.getItem(lockKey) === '1')
  const [error, setError] = useState('')
  const [result, setResult] = useState(false)
  const pending = useRef(false)
  const choose = async (selected?: File) => {
    if (pending.current || locked) return
    setFile(null); setPreview(null); setConfirmed(false); setError(''); setResult(false)
    if (!selected) return
    if (selected.size > maxSize) { setError('Choose a ZIP file no larger than 64 MiB.'); return }
    pending.current = true; setBusy(true)
    try { const data = await previewPluginCatalogRestore(selected); setFile(selected); setPreview(data) }
    catch (reason) { setError(message(reason)) }
    finally { pending.current = false; setBusy(false) }
  }
  const commit = async () => {
    if (pending.current || locked || localStorage.getItem(lockKey) === '1' || !file || !preview || preview.conflicts.length || !confirmed) { if (localStorage.getItem(lockKey) === '1') setLocked(true); return }
    pending.current = true; setBusy(true); setError('')
    localStorage.setItem(lockKey, '1'); setLocked(true)
    try {
      await commitPluginCatalogRestore(file)
      localStorage.removeItem(lockKey); setLocked(false); setPreview(null); setFile(null); setResult(true); onRestored()
    } catch (reason) {
      if (reason instanceof HttpError && [400, 409, 413, 415].includes(reason.status)) {
        localStorage.removeItem(lockKey); setLocked(false); setConfirmed(false); setError(message(reason))
      } else { setError(`${message(reason)} The restore outcome is unknown.`) }
    } finally { pending.current = false; setBusy(false) }
  }
  return <Modal titleId="restore-title" onClose={() => { if (!busy) onClose() }}><header className={styles.dialogHeader}><div><h2 id="restore-title">Restore Catalog (ZIP)</h2><p>Catalog backup · format version 2</p></div><button type="button" disabled={busy} onClick={onClose}>Close</button></header><div className={styles.importBody}>
    <p>Restore is allowed only into an empty catalog. Existing data is never replaced or merged.</p>
    {locked && <div role="alert"><p>The previous commit may have succeeded. Check the catalog and media storage with an operator before retrying; cleanup may be required.</p><button type="button" disabled={busy} onClick={() => { localStorage.removeItem(lockKey); setLocked(false); setFile(null); setPreview(null); setConfirmed(false); setError('') }}>I have checked the outcome</button></div>}
    <label className={styles.field}>Choose ZIP file (maximum 64 MiB)<input type="file" accept=".zip,application/zip" disabled={busy || locked} onChange={event => void choose(event.target.files?.[0])} /></label>
    {busy && <p role="status">Checking archive or restoring…</p>}
    {preview && <><h3>Preview</h3><p>{labels.map(key => `${key[0].toUpperCase()}${key.slice(1)}: ${preview.counts[key]}`).join(' · ')}</p><p>Preview reflects the current catalog and may become stale. The server checks the original ZIP again on commit.</p>{preview.conflicts.length > 0 ? <><h3>Conflicts</h3><table><thead><tr><th>Type</th><th>ID</th><th>Reason</th></tr></thead><tbody>{preview.conflicts.map((conflict, index) => <tr key={index}><td>{conflict.type}</td><td>{conflict.id}</td><td>{conflict.reason}</td></tr>)}</tbody></table></> : <p>No conflicts found. The catalog must still be empty when committing.</p>}<label><input type="checkbox" checked={confirmed} disabled={busy || locked} onChange={event => setConfirmed(event.target.checked)} /> I confirm restore into an empty catalog.</label><button type="button" disabled={busy || locked || !confirmed || preview.conflicts.length > 0} onClick={() => void commit()}>Confirm restore</button></>}
    {error && <p role="alert" className={styles.error}>{error}</p>}{result && <p role="status">Catalog restored.</p>}
  </div></Modal>
}

