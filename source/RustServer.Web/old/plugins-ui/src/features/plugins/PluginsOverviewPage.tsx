import { Link } from 'react-router-dom'

import { useControlPlane } from '../control-plane/ControlPlaneContext'
import { deriveServerState } from '../control-plane/model'
import styles from './Plugins.module.css'
import { formatTimestamp } from './model'
import { usePluginsEnvironment, useProductionDeployment } from './usePluginsResources'

const environmentLabels = {
  staging: 'Staging',
  production: 'Production',
} as const

function RustAdvisory() {
  const { serverStatus } = useControlPlane()

  if (serverStatus.status === 'loading') {
    return <dd>Loading status…</dd>
  }

  if (serverStatus.status === 'unavailable') {
    return <dd>Unavailable</dd>
  }

  const labels = { stopped: 'Stopped', starting: 'Starting', ready: 'Ready', inconsistent: 'Inconsistent / unavailable' }
  return <dd>{labels[deriveServerState(serverStatus.data)]}</dd>
}

export function PluginsOverviewPage() {
  const { environment } = usePluginsEnvironment()
  const isProductionEnvironment = environment.status === 'available' && environment.data.environment === 'production'
  const { deployment } = useProductionDeployment(isProductionEnvironment)

  return (
    <div className={styles.page}>
      <header className={styles.pageHeader}>
        <h1>Plugins</h1>
        <p>Manage Git-backed plugin packages, releases, and explicit deployments.</p>
      </header>

      <section aria-labelledby="plugins-context-heading" className={styles.section}>
        <h2 id="plugins-context-heading">Operational context</h2>
        <article className={styles.panel}>
          <dl className={styles.keyValueList}>
            <div>
              <dt>Environment</dt>
              {environment.status === 'loading' && <dd>Loading Environment…</dd>}
              {environment.status === 'unavailable' && <dd>Unavailable: {environment.message}</dd>}
              {environment.status === 'available' && <dd>{environmentLabels[environment.data.environment]}</dd>}
            </div>
            <div>
              <dt>Rust server</dt>
              <RustAdvisory />
            </div>
            {isProductionEnvironment && <div>
              <dt>Production deployment</dt>
              {deployment.status === 'loading' && <dd>Loading deployment…</dd>}
              {deployment.status === 'unavailable' && <dd>Unavailable: {deployment.message}</dd>}
              {deployment.status === 'available' && (
                <dd>
                  {deployment.data === null
                    ? 'No deployment recorded'
                    : `Release ${deployment.data.releaseId}, deployed ${formatTimestamp(deployment.data.deployedAt)}`}
                </dd>
              )}
            </div>}
          </dl>
          <p className={styles.notice}>Environment is read from the published Plugins API. This overview does not infer a deployed Source: the current contract has no general Staging deployment-status read, and a Source is established only by an applicable published baseline response. Environment changes are a separate explicit workflow; they do not deploy or start, stop, or restart Rust.</p>
        </article>
      </section>

      <nav aria-label="Plugins actions" className={styles.actions}>
        <Link className={styles.primaryLink} to="/plugins/deploy">Deploy</Link>
        <Link className={styles.secondaryLink} to="/plugins/updates">Updates</Link>
        <Link className={styles.secondaryLink} to="/plugins/releases">Releases</Link>
        <Link className={styles.secondaryLink} to="/plugins/runtime-changes">Review Runtime Changes</Link>
        <Link className={styles.secondaryLink} to="/server">Manage server</Link>
      </nav>
    </div>
  )
}
