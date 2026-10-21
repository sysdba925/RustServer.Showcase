import { useState } from 'react'
import { Link } from 'react-router-dom'

import {
  deployDevelopmentToStaging,
  deployReleaseToProduction,
  deployReleaseToStaging,
  HttpError,
  type ReleaseResponse,
  type ServiceEnvironment,
} from '../../shared/api'
import styles from './Plugins.module.css'
import { getOperatorErrorMessage, isOlderRelease, type ResourceState } from './model'
import {
  useDeploymentSource,
  usePluginsEnvironment,
  useReleases,
  type DeploymentSourceContext,
} from './usePluginsResources'

interface PendingDeployment {
  environment: ServiceEnvironment
  source: 'development' | 'release'
  release: ReleaseResponse | null
}

const environmentLabels = {
  staging: 'Staging',
  production: 'Production',
} as const

function confirmationTitle(pending: PendingDeployment, productionReleaseId: string | null): string {
  if (pending.source === 'development') {
    return 'Deploy Development to Staging'
  }

  if (pending.environment === 'staging') {
    return `Deploy Release ${pending.release?.releaseId} to Staging`
  }

  return pending.release && productionReleaseId && isOlderRelease(pending.release.releaseId, productionReleaseId)
    ? `Rollback Production to Release ${pending.release.releaseId}`
    : `Deploy Release ${pending.release?.releaseId} to Production`
}

function sourceLabel(source: ResourceState<DeploymentSourceContext>): string {
  if (source.status === 'loading') {
    return 'Loading Source…'
  }

  if (source.status === 'unavailable') {
    return `Unknown / unavailable: ${source.message}`
  }

  if (source.data.source === 'development') {
    return 'Development'
  }

  if (source.data.source === 'release') {
    return source.data.releaseId === null
      ? 'Release (ID unavailable)'
      : `Release ${source.data.releaseId}`
  }

  return 'Unknown / unavailable'
}

function environmentChangeError(error: unknown, nextEnvironment: ServiceEnvironment): string {
  if (error instanceof HttpError && error.status === 409 && nextEnvironment === 'production') {
    const detail = error.problemDetails?.detail?.trim() || error.problemDetails?.title?.trim()
    return detail
      ? `Environment change blocked. Deploy a valid Release before switching to Production. ${detail}`
      : 'Environment change blocked. Deploy a valid Release before switching to Production.'
  }

  return getOperatorErrorMessage(error, {
    conflict: 'Environment change is blocked',
    forbidden: 'Environment change is forbidden',
    invalid: 'Environment change request is invalid',
  })
}

export function PluginsDeployPage() {
  const { releases } = useReleases()
  const { environment, change: changeEnvironment } = usePluginsEnvironment()
  const { source, recordDeployment } = useDeploymentSource(environment)
  const [pending, setPending] = useState<PendingDeployment | null>(null)
  const [confirmReleaseId, setConfirmReleaseId] = useState('')
  const [mutating, setMutating] = useState(false)
  const [mutationError, setMutationError] = useState<string | null>(null)
  const [mutationSuccess, setMutationSuccess] = useState<string | null>(null)
  const [runtimeChangesConflict, setRuntimeChangesConflict] = useState(false)

  const currentEnvironment = environment.status === 'available'
    ? environment.data.environment
    : null
  const productionReleaseId = currentEnvironment === 'production' && source.status === 'available'
    ? source.data.releaseId
    : null
  const environmentChangeTarget = currentEnvironment === 'staging' ? 'production' : 'staging'

  function review(next: PendingDeployment) {
    setMutationError(null)
    setMutationSuccess(null)
    setRuntimeChangesConflict(false)
    setConfirmReleaseId('')
    setPending(next)
  }

  async function switchEnvironment() {
    if (currentEnvironment === null || mutating || pending !== null) {
      return
    }

    setMutating(true)
    setMutationError(null)
    setMutationSuccess(null)
    setRuntimeChangesConflict(false)

    try {
      const changed = await changeEnvironment(environmentChangeTarget)
      setMutationSuccess(
        `Environment changed to ${environmentLabels[changed.environment]}. The current baseline was not redeployed.`,
      )
    } catch (error) {
      setMutationError(environmentChangeError(error, environmentChangeTarget))
    } finally {
      setMutating(false)
    }
  }

  async function deploy() {
    if (!pending) {
      return
    }

    setMutating(true)
    setMutationError(null)
    setMutationSuccess(null)
    setRuntimeChangesConflict(false)

    try {
      if (pending.source === 'development') {
        const deployment = await deployDevelopmentToStaging()
        recordDeployment('staging', deployment.source, null)
      } else if (pending.environment === 'staging') {
        const deployment = await deployReleaseToStaging(pending.release!.releaseId)
        recordDeployment('staging', deployment.source, deployment.releaseId)
      } else {
        const deployment = await deployReleaseToProduction(pending.release!.releaseId)
        recordDeployment('production', deployment.source, deployment.releaseId)
      }

      setMutationSuccess(`${confirmationTitle(pending, productionReleaseId)} completed.`)
      setPending(null)
    } catch (error) {
      setMutationError(getOperatorErrorMessage(error))
      setRuntimeChangesConflict(
        error instanceof HttpError &&
          error.status === 409 &&
          /managed runtime changes/i.test(error.problemDetails?.detail ?? error.problemDetails?.title ?? ''),
      )
    } finally {
      setMutating(false)
    }
  }

  const productionConfirmationMatches = pending?.environment !== 'production'
    || pending.release === null
    || confirmReleaseId === pending.release.releaseId
  const actionsDisabled = pending !== null || mutating

  return (
    <div className={styles.page}>
      <header className={styles.pageHeader}>
        <div><h1>Deploy</h1><p>Deploy a permitted Source to the single Rust target controlled by this Plugins instance.</p></div>
        <Link className={styles.secondaryLink} to="/plugins/releases">View releases</Link>
      </header>

      {mutationError && <p className={styles.error} role="alert">{mutationError}{runtimeChangesConflict && <> <Link to="/plugins/runtime-changes">Review Runtime Changes</Link></>}</p>}
      {mutationSuccess && <p className={styles.success} role="status">{mutationSuccess}</p>}

      <section aria-labelledby="deployment-environment-heading" className={currentEnvironment === 'production' ? `${styles.section} ${styles.productionSection}` : styles.section}>
        {currentEnvironment === 'production' && <div className={styles.productionBanner}><strong>Production Environment</strong><span>Release deployments only</span></div>}
        <h2 id="deployment-environment-heading">Current Environment</h2>
        <article className={styles.panel}>
          {environment.status === 'loading' && <p className={styles.loading} role="status">Loading Environment…</p>}
          {environment.status === 'unavailable' && <p className={styles.error} role="alert">Environment unavailable. Deploy actions are hidden. {environment.message}</p>}
          {environment.status === 'available' && <>
            <dl className={styles.keyValueList}>
              <div><dt>Environment</dt><dd>{environmentLabels[currentEnvironment!]}</dd></div>
              <div><dt>Deployed Source</dt><dd>{sourceLabel(source)}</dd></div>
            </dl>
            <p className={styles.notice}>Changing Environment does not deploy, select a Release, or start, stop, or restart Rust.</p>
            <div className={styles.actions}>
              <button disabled={actionsDisabled} onClick={() => void switchEnvironment()} type="button">
                {mutating ? 'Changing Environment…' : `Switch to ${environmentLabels[environmentChangeTarget]}`}
              </button>
            </div>
          </>}
        </article>
      </section>

      {currentEnvironment === 'staging' && <section aria-labelledby="staging-deployment-heading" className={styles.section}>
        <h2 id="staging-deployment-heading">Deploy in Staging Environment</h2>
        <article className={styles.panel}>
          <h3>Development</h3>
          <p className={styles.sectionDescription}>Deploy the current clean Development Git state. Selecting it does not deploy files.</p>
          <button disabled={actionsDisabled} onClick={() => review({ environment: 'staging', source: 'development', release: null })} type="button">Review Development deployment</button>
        </article>
        <article className={`${styles.panel} ${styles.panelSpacing}`}>
          <h3>Release</h3>
          <p className={styles.sectionDescription}>Deploy one immutable Release to the current target.</p>
          <ReleaseChoices disabled={actionsDisabled} releases={releases} onReview={(release) => review({ environment: 'staging', source: 'release', release })} />
        </article>
      </section>}

      {currentEnvironment === 'production' && <section aria-labelledby="production-deployment-heading" className={`${styles.section} ${styles.productionSection}`}>
        <h2 id="production-deployment-heading">Deploy in Production Environment</h2>
        <article className={styles.panel}>
          <p className={styles.sectionDescription}>Development is not available in Production. An older Release is a rollback through the same deployment operation.</p>
          <ReleaseChoices disabled={actionsDisabled} releases={releases} onReview={(release) => review({ environment: 'production', source: 'release', release })} />
        </article>
      </section>}

      {pending && (
        <section aria-labelledby="deployment-confirmation-heading" className={styles.confirmation}>
          <h2 id="deployment-confirmation-heading">Confirm deployment</h2>
          <p>{confirmationTitle(pending, productionReleaseId)}</p>
          <p className={styles.sectionDescription}>Environment: <strong>{environmentLabels[pending.environment]}</strong>{pending.release && <> · Release ID: <strong>{pending.release.releaseId}</strong></>}</p>
          {pending.environment === 'production' && pending.release && (
            <label className={styles.confirmationField}>Type release ID <strong>{pending.release.releaseId}</strong> to confirm
              <input aria-label="Confirm release ID" onChange={(event) => setConfirmReleaseId(event.target.value)} value={confirmReleaseId} />
            </label>
          )}
          <div className={styles.actions}>
            <button className={pending.environment === 'production' ? styles.dangerButton : undefined} disabled={mutating || !productionConfirmationMatches} onClick={() => void deploy()} type="button">
              {mutating ? 'Deploying…' : confirmationTitle(pending, productionReleaseId)}
            </button>
            <button className={styles.cancelButton} disabled={mutating} onClick={() => setPending(null)} type="button">Cancel</button>
          </div>
        </section>
      )}
    </div>
  )
}

function ReleaseChoices({ disabled = false, releases, onReview }: { disabled?: boolean; releases: ReturnType<typeof useReleases>['releases']; onReview: (release: ReleaseResponse) => void }) {
  if (releases.status === 'loading') return <p className={styles.loading} role="status">Loading releases…</p>
  if (releases.status === 'unavailable') return <p className={styles.error} role="alert">Release catalog unavailable. {releases.message}</p>
  if (releases.data.length === 0) return <p className={styles.empty}>No Release is available to deploy.</p>
  return <ul className={styles.releaseChoices}>{releases.data.map((release) => <li key={release.releaseId}><span>Release {release.releaseId}</span><button disabled={disabled} onClick={() => onReview(release)} type="button">Review</button></li>)}</ul>
}
