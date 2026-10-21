import { useState } from 'react'
import { applyImport, parseImport, unknownItems } from './importData'
import type { ImportRow } from './importData'
import type { PrototypeLibrary } from './model'
import { Modal } from './Modal'
import styles from './prototype.module.css'

const example = { schemaVersion: 1, plugins: [{ name: 'Example Shop Plugin', sources: [{ site: 'Example Site', url: 'https://example.com/plugins/shop' }], author: 'Example Author', description: '## Overview\nExample **Markdown** description.\n\n```csharp\n// Code example\n```', notes: 'Interested, but not purchased yet.', price: { amount: 14.99, currency: 'USD' }, tags: ['Nice to have'], folders: ['Shop', 'Experiments'] }, { name: 'Example Purchased Plugin', price: { amount: 9.5, currency: 'EUR' }, tags: ['Purchased', 'Archived'], folders: ['Archive'] }, { name: 'Example Minimal Plugin' }] }

export function ImportDialog({ library, onImport, onClose }: { library: PrototypeLibrary; onImport: (value: PrototypeLibrary) => void; onClose: () => void }) {
  const [rows, setRows] = useState<ImportRow[]>([])
  const [error, setError] = useState('')
  const [acceptNew, setAcceptNew] = useState(false)
  const [result, setResult] = useState('')
  const unknown = unknownItems(rows, library)
  const hasUnknown = Object.values(unknown).some(items => items.length)
  const valid = rows.length > 0 && rows.every(row => !row.errors.length)
  const count = rows.filter(row => row.value && !row.duplicates.length).length
  const download = () => {
    const url = URL.createObjectURL(new Blob([JSON.stringify(example, null, 2)], { type: 'application/json' }))
    const link = document.createElement('a'); link.href = url; link.download = 'plugin-catalog-example.json'; link.click(); URL.revokeObjectURL(url)
  }
  return <Modal titleId="import-title" onClose={onClose}><header className={styles.dialogHeader}><div><h2 id="import-title">Import JSON</h2><p>Review one collection before adding it to this browser library.</p></div><button type="button" onClick={onClose}>Close</button></header><div className={styles.importBody}>
    <p><strong>Required:</strong> schemaVersion (1), plugins (non-empty array), plugin name. <strong>Optional:</strong> sources (url, site), author, description (Markdown), notes, price (amount, currency), tags, folders. No IDs, slugs, versions or media files.</p>
    <button type="button" onClick={download}>Download example JSON</button>
    <label className={styles.field}>Choose JSON file<input type="file" accept=".json,application/json" onChange={async event => { const file = event.target.files?.[0]; setResult(''); setAcceptNew(false); setRows([]); setError(''); if (!file) return; try { const parsed = parseImport(await file.text(), library); setRows(parsed.rows); setError(parsed.error ?? '') } catch { setError('Could not read the file.') } }} /></label>
    {error && <p className={styles.error} role="alert">{error}</p>}
    {result && <p role="status">{result}</p>}
    {rows.length > 0 && <><h3>Preview · {rows.length} records</h3><p>Matching names, normalized names or source URLs are marked as possible duplicates and will be skipped; existing records are never overwritten.</p><div className={styles.importRows}>{rows.map(row => <details key={row.index} open={!!row.errors.length || !!row.duplicates.length}><summary>#{row.index + 1} · {row.value?.name || 'Invalid record'}{row.duplicates.length ? ' · Possible duplicate' : row.errors.length ? ' · Invalid' : ' · Ready'}</summary>{row.errors.map(message => <p className={styles.error} key={message}>{message}</p>)}{row.duplicates.length > 0 && <p>Matches: {row.duplicates.join(', ')}</p>}{row.value && <pre>{JSON.stringify(row.value, null, 2)}</pre>}</details>)}</div>
      {hasUnknown && <div><p>New sources: {unknown.sites.join(', ') || '—'}<br />New tags: {unknown.tags.join(', ') || '—'}<br />New folders: {unknown.folders.join(', ') || '—'}</p><label><input type="checkbox" checked={acceptNew} onChange={event => setAcceptNew(event.target.checked)} /> Create these missing library items</label></div>}
      {!valid && <p className={styles.error}>Fix every invalid record in the file before importing.</p>}
      <button type="button" disabled={!valid || !count || (hasUnknown && !acceptNew)} onClick={() => { onImport(applyImport(rows, library)); setResult(`Imported ${count}; skipped ${rows.length - count}; errors 0.`); setRows([]); setAcceptNew(false) }}>Confirm import ({count})</button>
    </>}
  </div></Modal>
}
