import { useCallback, useEffect, useMemo, useState } from 'react'
import { Link, useParams } from 'react-router-dom'

import {
  commitUpdatePackage,
  getIncomingChangeDiff,
  getIncomingChanges,
  getUpdatePackage,
  HttpError,
  type CommitUpdatePackageResponse,
  type IncomingChangeResponse,
  type IncomingChangesResponse,
  type UpdatePackageResponse,
} from '../../shared/api'
import { CommitUpdateReview } from './components/CommitUpdateReview'
import { IncomingChangesPanel } from './components/IncomingChangesPanel'
import { IncomingDiffPanel } from './components/IncomingDiffPanel'
import { UpdatePackageSummary } from './components/UpdatePackageSummary'
import styles from './Plugins.module.css'
import { getOperatorErrorMessage, isIncomingChangeCommitEligible } from './model'

const packageErrors = {
  conflict: 'The incoming comparison is stale or conflicts with the current workspace',
  forbidden: 'This update-package action is unavailable',
  notFound: 'This update package is no longer available or the ID is unknown',
  invalid: 'The update-package request is invalid',
}

export function PluginsUpdatePackagePage() {
  const { packageId } = useParams()
  const [updatePackage, setUpdatePackage] = useState<UpdatePackageResponse | null>(null)
  const [comparison, setComparison] = useState<IncomingChangesResponse | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [selectedPaths, setSelectedPaths] = useState<Set<string>>(new Set())
  const [diff, setDiff] = useState<Awaited<ReturnType<typeof getIncomingChangeDiff>> | null>(null)
  const [diffLoading, setDiffLoading] = useState(false)
  const [diffError, setDiffError] = useState<string | null>(null)
  const [message, setMessage] = useState('Apply uploaded update package')
  const [committing, setCommitting] = useState(false)
  const [commitResult, setCommitResult] = useState<CommitUpdatePackageResponse | null>(null)

  const refresh = useCallback(async (clearTransient = false) => {
    if (!packageId) return
    setLoading(true)
    setError(null)
    try {
      const packageState = await getUpdatePackage(packageId)
      setUpdatePackage(packageState)
      if (packageState.status === 'inspected') {
        const incomingChanges = await getIncomingChanges(packageId)
        setComparison(incomingChanges)
      } else {
        setComparison(null)
      }
      if (clearTransient) {
        setSelectedPaths(new Set())
        setDiff(null)
        setDiffError(null)
      }
    } catch (loadError) {
      setError(getOperatorErrorMessage(loadError, packageErrors))
    } finally {
      setLoading(false)
    }
  }, [packageId])

  useEffect(() => {
    void Promise.resolve().then(() => refresh(true))
  }, [refresh])

  const selectedChanges = useMemo(() => comparison?.changes.filter((change) => selectedPaths.has(change.path)) ?? [], [comparison, selectedPaths])

  function selectPath(path: string, selected: boolean) {
    setSelectedPaths((current) => {
      const next = new Set(current)
      if (selected) next.add(path)
      else next.delete(path)
      return next
    })
  }

  function selectAll() {
    if (!comparison) return
    setSelectedPaths(new Set(comparison.changes.filter(isIncomingChangeCommitEligible).map((change) => change.path)))
  }

  async function handleConflict(operationError: unknown) {
    if (operationError instanceof HttpError && operationError.status === 409) {
      await refresh(true)
      setError(getOperatorErrorMessage(operationError, packageErrors))
      return true
    }
    return false
  }

  async function viewDiff(change: IncomingChangeResponse) {
    if (!packageId || !comparison || !change.textDiffAvailable) return
    const workspaceHead = comparison.workspaceHeads.find((head) => head.repository === change.repository)?.head
    if (!workspaceHead) {
      setDiff(null)
      setDiffError('The comparison did not provide a workspace head for this repository.')
      return
    }

    setDiffLoading(true)
    setDiffError(null)
    setDiff(null)
    try {
      setDiff(await getIncomingChangeDiff(packageId, change.path, workspaceHead))
    } catch (diffRequestError) {
      if (!(await handleConflict(diffRequestError))) setDiffError(getOperatorErrorMessage(diffRequestError, packageErrors))
    } finally {
      setDiffLoading(false)
    }
  }

  async function commit() {
    if (!packageId || !comparison || selectedChanges.length === 0 || message.trim().length === 0 || committing) return
    setCommitting(true)
    setError(null)
    try {
      const expectedWorkspaceHeads = Object.fromEntries(comparison.workspaceHeads.map((head) => [head.repository, head.head]))
      const result = await commitUpdatePackage(packageId, {
        acceptedPaths: selectedChanges.map((change) => change.path),
        expectedWorkspaceHeads,
        message: message.trim(),
      })
      setCommitResult(result)
      await refresh(true)
    } catch (commitError) {
      const wasConflict = await handleConflict(commitError)
      if (!wasConflict) setError(getOperatorErrorMessage(commitError, packageErrors))
    } finally {
      setCommitting(false)
    }
  }

  if (!packageId) return <div className={styles.page}><p className={styles.error} role="alert">An update package ID is required.</p></div>

  return (
    <div className={styles.page}>
      <header className={styles.pageHeader}>
        <div><h1>Update package</h1><p>Inspect the server-owned package state and commit selected Incoming Changes as one operation.</p></div>
        <Link className={styles.secondaryLink} to="/plugins/updates">Back to Updates</Link>
      </header>

      {loading && <p className={styles.loading} role="status">Loading update package…</p>}
      {error && <p className={styles.error} role="alert">{error}</p>}
      {updatePackage && <UpdatePackageSummary updatePackage={updatePackage} />}

      {updatePackage?.status === 'received' && <p className={styles.notice}>The package has been received but is not yet available for Incoming Changes. Reload this package when inspection is complete.</p>}
      {updatePackage?.status === 'failed' && <p className={styles.error} role="alert">Package inspection failed. No commit path is available for this package.</p>}
      {updatePackage && (updatePackage.status === 'committed' || commitResult !== null) && <CommittedResult result={commitResult} updatePackage={updatePackage} />}

      {updatePackage?.status === 'inspected' && comparison && commitResult === null && (
        <>
          <IncomingChangesPanel changes={comparison.changes} onSelectAll={selectAll} onSelectionChange={selectPath} onViewDiff={(change) => void viewDiff(change)} selectedPaths={selectedPaths} />
          <IncomingDiffPanel diff={diff} error={diffError} loading={diffLoading} onClose={() => { setDiff(null); setDiffError(null) }} />
          <CommitUpdateReview committing={committing} filename={updatePackage.originalFileName} message={message} onCommit={() => void commit()} onMessageChange={setMessage} selectedChanges={selectedChanges} />
        </>
      )}
    </div>
  )
}

function CommittedResult({ result, updatePackage }: { result: CommitUpdatePackageResponse | null; updatePackage: UpdatePackageResponse }) {
  const commits = result?.commits ?? updatePackage.commits
  return (
    <section aria-labelledby="committed-update-heading" className={styles.confirmation}>
      <h2 id="committed-update-heading">Update package committed</h2>
      <p>Selected paths were committed as one UpdatePackage operation.</p>
      <ul className={styles.commitList}>{commits.map((commit) => <li key={commit.repository}><strong>{commit.repository}</strong>: <code>{commit.commitSha}</code> ({commit.committedPaths.length} paths)</li>)}</ul>
      <nav aria-label="Next update package actions" className={styles.actions}>
        <Link className={styles.primaryLink} to="/plugins/deploy">Deploy Development</Link>
        <Link className={styles.secondaryLink} to="/plugins/releases">View Releases</Link>
        <Link className={styles.secondaryLink} to="/plugins/updates">Back to Updates</Link>
      </nav>
    </section>
  )
}
