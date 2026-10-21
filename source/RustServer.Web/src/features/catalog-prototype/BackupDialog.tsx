import { useState } from 'react'
import { createBackup, readBackup } from './backup'
import type { BackupPreview } from './backup'
import type { PrototypeLibrary } from './model'
import { Modal } from './Modal'
import styles from './prototype.module.css'

export function BackupDialog({ mode, library, onRestore, onClose }: { mode: 'export' | 'restore'; library: PrototypeLibrary; onRestore: (library: PrototypeLibrary) => boolean; onClose: () => void }) {
  const [preview, setPreview] = useState<BackupPreview | null>(null)
  const [error, setError] = useState('')
  const [result, setResult] = useState('')
  const [confirm, setConfirm] = useState(false)
  const [busy, setBusy] = useState(false)
  const [report, setReport] = useState<{ embedded: number; external: number; unavailable: number } | null>(null)
  const exportCatalog = async () => {
    setBusy(true); setError(''); setResult(''); setReport(null)
    try {
      const { bytes, manifest } = await createBackup(library)
      const data = new Blob([new Uint8Array(bytes)], { type: 'application/zip' })
      const url = URL.createObjectURL(data)
      const link = document.createElement('a'); link.href = url; link.download = 'plugin-catalog-backup.zip'; link.click(); URL.revokeObjectURL(url)
      setReport({ embedded: manifest.media.length, external: manifest.externalMediaCount, unavailable: manifest.unavailableMedia.length })
      setResult('Archive prepared for download.')
    } catch (reason) { setError(reason instanceof Error ? reason.message : 'Could not create archive.') } finally { setBusy(false) }
  }
  const load = async (file: File) => {
    setBusy(true); setPreview(null); setError(''); setResult(''); setConfirm(false)
    try { setPreview(await readBackup(file)) } catch (reason) { setError(reason instanceof Error ? `Invalid backup: ${reason.message}` : 'Invalid backup archive.') } finally { setBusy(false) }
  }
  return <Modal titleId="backup-title" onClose={onClose}><header className={styles.dialogHeader}><div><h2 id="backup-title">{mode === 'export' ? 'Export Catalog (ZIP)' : 'Restore Catalog (ZIP)'}</h2><p>Prototype backup · format version 1</p></div><button type="button" onClick={onClose}>Close</button></header><div className={styles.importBody}>
    {mode === 'export' ? <><p>Exports the entire local library, including saved views and organization settings, regardless of filters or the current page.</p><p>External media URLs are saved as links and are not downloaded. Available local PNG, JPEG, GIF and WebP images are included in the archive; unavailable local media is reported.</p><button type="button" disabled={busy} onClick={exportCatalog}>{busy ? 'Preparing…' : 'Download ZIP'}</button>{report && <p>Included local images: {report.embedded}. External links only: {report.external}. Unavailable local references: {report.unavailable}.{report.unavailable > 0 && ' The archive does not contain these images.'}</p>}</> : <><p>Select a backup to inspect it before replacing the current library. This is a full replacement, not a merge.</p><label className={styles.field}>Choose ZIP file<input type="file" accept=".zip,application/zip" onChange={event => { const file = event.target.files?.[0]; if (file) void load(file); else { setPreview(null); setError('') } }} /></label>{busy && <p>Checking archive…</p>}{preview && <><h3>Preview</h3><p>{preview.library.plugins.length} plugins · {preview.library.sites.length} sources · {preview.library.tags.length} statuses · {preview.library.folders.length} folders · {preview.library.views.length} saved views</p><p>Included local images: {preview.manifest.media.length}. External links only: {preview.manifest.externalMediaCount}. Unavailable local references: {preview.manifest.unavailableMedia.length}.</p>{preview.manifest.unavailableMedia.length > 0 && <p className={styles.error}>Some local images were unavailable during export and are not included. Their references may no longer work.</p>}<label><input type="checkbox" checked={confirm} onChange={event => setConfirm(event.target.checked)} /> I understand this replaces the entire current library.</label><button type="button" disabled={!confirm} onClick={() => { if (onRestore(preview.library)) { setPreview(null); setResult('Catalog restored in this browser.') } else setError('Browser storage is unavailable or full. The current library was not replaced.') }}>Replace catalog</button></>}</>}
    {error && <p role="alert" className={styles.error}>{error}</p>}{result && <p role="status">{result}</p>}
  </div></Modal>
}
