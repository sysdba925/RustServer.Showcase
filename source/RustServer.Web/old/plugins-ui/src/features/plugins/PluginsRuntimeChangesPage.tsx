import { useCallback, useEffect, useMemo, useState } from 'react'
import { Link } from 'react-router-dom'

import {
  discardStagingRuntimeChanges,
  getStagingRuntimeChangeDiff,
  getStagingRuntimeChanges,
  HttpError,
  importStagingRuntimeChanges,
  type RuntimeChangeResponse,
  type StagingRuntimeChangeDiscardResponse,
  type StagingRuntimeChangeImportResponse,
  type StagingRuntimeChangesResponse,
} from '../../shared/api'
import { RuntimeChangesPanel } from './components/RuntimeChangesPanel'
import { RuntimeDiffPanel } from './components/RuntimeDiffPanel'
import { RuntimeDiscardReview, RuntimeImportReview } from './components/RuntimeChangeReview'
import styles from './Plugins.module.css'
import { getOperatorErrorMessage, shortSha } from './model'

type PendingAction = 'import' | 'discard' | null

const runtimeErrors = {
  conflict: 'Runtime changes conflict',
  forbidden: 'Runtime changes unavailable',
  invalid: 'Runtime change request invalid',
}

function sourceLabel(source: string): string {
  if (source === 'development') return 'Development'
  if (source === 'release') return 'Release'
  return '<unknown source>'
}

function unavailableMessage(error: HttpError): string {
  const detail = error.problemDetails?.detail?.trim() || error.problemDetails?.title?.trim()
  return detail
    ? `Runtime Changes are available only for the permitted Staging context. ${detail}`
    : 'Runtime Changes are available only for the permitted Staging context.'
}

/** UC-05–07 surface. All runtime state, selections, diffs, and mutations are local to this feature. */
export function PluginsRuntimeChangesPage() {
  const [comparison, setComparison] = useState<StagingRuntimeChangesResponse | null>(null)
  const [loading, setLoading] = useState(true)
  const [unavailable, setUnavailable] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [selectedChangeTokens, setSelectedChangeTokens] = useState<Map<string, string>>(new Map())
  const [diff, setDiff] = useState<Awaited<ReturnType<typeof getStagingRuntimeChangeDiff>> | null>(null)
  const [diffLoading, setDiffLoading] = useState(false)
  const [diffError, setDiffError] = useState<string | null>(null)
  const [pendingAction, setPendingAction] = useState<PendingAction>(null)
  const [message, setMessage] = useState('Import Staging runtime changes into Development')
  const [importing, setImporting] = useState(false)
  const [discarding, setDiscarding] = useState(false)
  const [importResult, setImportResult] = useState<StagingRuntimeChangeImportResponse | null>(null)
  const [discardResult, setDiscardResult] = useState<StagingRuntimeChangeDiscardResponse | null>(null)

  const clearTransientState = useCallback((keepResult = false) => {
    setSelectedChangeTokens(new Map())
    setDiff(null)
    setDiffError(null)
    setPendingAction(null)
    if (!keepResult) {
      setImportResult(null)
      setDiscardResult(null)
    }
  }, [])

  const refresh = useCallback(async (keepResult = false) => {
    clearTransientState(keepResult)
    setLoading(true)
    setError(null)
    setUnavailable(false)
    setComparison(null)
    try {
      setComparison(await getStagingRuntimeChanges())
    } catch (loadError) {
      if (loadError instanceof HttpError && loadError.status === 403) {
        setUnavailable(true)
        setError(unavailableMessage(loadError))
      } else {
        setError(getOperatorErrorMessage(loadError, runtimeErrors))
      }
    } finally {
      setLoading(false)
    }
  }, [clearTransientState])

  useEffect(() => {
    void Promise.resolve().then(() => refresh())
  }, [refresh])

  const selectedChanges = useMemo(
    () => comparison?.changes.filter((change) => selectedChangeTokens.get(change.path) === change.changeToken) ?? [],
    [comparison, selectedChangeTokens],
  )
  const selectedPaths = useMemo(() => new Set(selectedChanges.map((change) => change.path)), [selectedChanges])
  const isDevelopmentBaseline = comparison?.source === 'development'
  const hasSupportedDiscardBaseline = comparison?.source === 'development' || comparison?.source === 'release'

  function selectPath(change: RuntimeChangeResponse, selected: boolean) {
    setSelectedChangeTokens((current) => {
      const next = new Map(current)
      if (selected) next.set(change.path, change.changeToken)
      else next.delete(change.path)
      return next
    })
    setPendingAction(null)
  }

  async function handleConflict(operationError: unknown): Promise<boolean> {
    if (operationError instanceof HttpError && operationError.status === 409) {
      await refresh()
      setError('Runtime state changed since it was reviewed. The comparison has been refreshed. Review and select the current changes again.')
      return true
    }
    return false
  }

  async function viewDiff(change: RuntimeChangeResponse) {
    if (!change.textDiffAvailable) return
    setDiffLoading(true)
    setDiffError(null)
    setDiff(null)
    try {
      setDiff(await getStagingRuntimeChangeDiff(change.path))
    } catch (diffRequestError) {
      if (!(await handleConflict(diffRequestError))) setDiffError(getOperatorErrorMessage(diffRequestError, runtimeErrors))
    } finally {
      setDiffLoading(false)
    }
  }

  async function importChanges() {
    if (!isDevelopmentBaseline || selectedChanges.length === 0 || message.trim().length === 0 || importing) return
    setImporting(true)
    setError(null)
    setDiscardResult(null)
    try {
      const result = await importStagingRuntimeChanges({
        message: message.trim(),
        changes: selectedChanges.map(({ path, changeToken }) => ({ path, expectedChangeToken: changeToken })),
      })
      setImportResult(result)
      await refresh(true)
    } catch (importError) {
      if (!(await handleConflict(importError))) setError(getOperatorErrorMessage(importError, runtimeErrors))
    } finally {
      setImporting(false)
    }
  }

  async function discardChanges() {
    if (!hasSupportedDiscardBaseline || selectedChanges.length === 0 || discarding) return
    setDiscarding(true)
    setError(null)
    setImportResult(null)
    try {
      const result = await discardStagingRuntimeChanges({
        changes: selectedChanges.map(({ path, changeToken }) => ({ path, expectedChangeToken: changeToken })),
      })
      setDiscardResult(result)
      await refresh(true)
    } catch (discardError) {
      if (!(await handleConflict(discardError))) setError(getOperatorErrorMessage(discardError, runtimeErrors))
    } finally {
      setDiscarding(false)
    }
  }

  return (
    <div className={styles.page}>
      <header className={styles.pageHeader}>
        <div><h1>Runtime Changes</h1><p>Review Staging changes against its exact deployed baseline.</p></div>
        <div className={styles.actions}><button disabled={loading} onClick={() => void refresh()} type="button">Refresh</button><Link className={styles.secondaryLink} to="/plugins/deploy">Back to Deploy</Link></div>
      </header>

      {loading && <p className={styles.loading} role="status">Loading runtime comparison…</p>}
      {error && <p className={styles.error} role="alert">{error}</p>}
      {unavailable ? null : comparison && (
        <>
          <section aria-labelledby="runtime-baseline-heading" className={styles.section}>
            <h2 id="runtime-baseline-heading">Deployed baseline</h2>
            <article className={styles.panel}><dl className={styles.keyValueList}>
              <div><dt>Source</dt><dd>{sourceLabel(comparison.source)}</dd></div>
              <div><dt>Plugins SHA</dt><dd><code title={comparison.pluginsSha}>{shortSha(comparison.pluginsSha)}</code></dd></div>
              <div><dt>Config SHA</dt><dd><code title={comparison.oxideConfigsSha}>{shortSha(comparison.oxideConfigsSha)}</code></dd></div>
            </dl></article>
          </section>

          {comparison.changes.length === 0 ? <section className={styles.section}><p className={styles.empty}>No managed runtime changes detected.</p></section> : <>
            <RuntimeChangesPanel changes={comparison.changes} onSelectionChange={selectPath} onViewDiff={(change) => void viewDiff(change)} selectedPaths={selectedPaths} />
            <RuntimeDiffPanel diff={diff} error={diffError} loading={diffLoading} onClose={() => { setDiff(null); setDiffError(null) }} />
            <section aria-label="Runtime change actions" className={styles.actions}>
              <button disabled={!isDevelopmentBaseline || selectedChanges.length === 0} onClick={() => setPendingAction('import')} type="button">Review Import</button>
              <button className={styles.dangerButton} disabled={!hasSupportedDiscardBaseline || selectedChanges.length === 0} onClick={() => setPendingAction('discard')} type="button">Review Discard</button>
            </section>
            {comparison.source === 'release' && <p className={styles.notice}>Runtime changes from a Release baseline cannot be imported into Development.</p>}
            {!isDevelopmentBaseline && comparison.source !== 'release' && <p className={styles.notice}>Import is unavailable because this runtime baseline source is not supported by the published workflow.</p>}
            {!hasSupportedDiscardBaseline && <p className={styles.notice}>Discard is unavailable because this runtime baseline source is not supported by the published workflow.</p>}
            {pendingAction === 'import' && <RuntimeImportReview importing={importing} message={message} onCancel={() => setPendingAction(null)} onConfirm={() => void importChanges()} onMessageChange={setMessage} selectedChanges={selectedChanges} />}
            {pendingAction === 'discard' && <RuntimeDiscardReview discarding={discarding} onCancel={() => setPendingAction(null)} onConfirm={() => void discardChanges()} selectedChanges={selectedChanges} />}
          </>}
          {importResult && <ImportResult result={importResult} />}
          {discardResult && <p className={styles.success} role="status">Discarded {discardResult.discardedPaths.length} runtime change{discardResult.discardedPaths.length === 1 ? '' : 's'}: {discardResult.discardedPaths.join(', ')}</p>}
        </>
      )}
    </div>
  )
}

function ImportResult({ result }: { result: StagingRuntimeChangeImportResponse }) {
  return (
    <section aria-labelledby="runtime-import-result-heading" className={styles.confirmation}>
      <h2 id="runtime-import-result-heading">Runtime Changes imported into Development</h2>
      <p>Imported into Development. The Staging deployment baseline has not changed. These runtime changes remain detected until discarded or otherwise resolved by the documented workflow.</p>
      <ul className={styles.commitList}>{result.commits.map((commit) => <li key={commit.repository}><strong>{commit.repository}</strong>: <code>{commit.commitSha}</code> ({commit.committedPaths.join(', ')})</li>)}</ul>
      <p className={styles.sectionDescription}>Next step: Discard the imported runtime changes before deploying the new Development baseline.</p>
    </section>
  )
}
