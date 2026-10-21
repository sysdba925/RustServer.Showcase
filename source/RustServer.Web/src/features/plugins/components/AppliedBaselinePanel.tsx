import { Panel } from '../../../shared/ui/Panel'
import type { PluginsDashboardState } from '../model/plugins-dashboard'
import styles from '../PluginsPage.module.css'

function formatAppliedAt(value: string): string {
  const parsed = new Date(value)
  return Number.isNaN(parsed.getTime())
    ? 'Invalid timestamp'
    : `${new Intl.DateTimeFormat('en-GB', {
        dateStyle: 'medium',
        timeStyle: 'short',
        timeZone: 'UTC',
      }).format(parsed)} UTC`
}

function RuntimeSummary({ runtimeDiff }: Pick<PluginsDashboardState, 'runtimeDiff'>) {
  if (runtimeDiff.status === 'loading') return <strong>Loading…</strong>
  if (runtimeDiff.status === 'unavailable') return <strong className={styles.bad}>Unknown / unavailable</strong>
  if (runtimeDiff.data.status === 'notInitialized') return <strong>Not initialized</strong>

  return runtimeDiff.data.changes.length === 0
    ? <strong className={styles.good}>Up to date</strong>
    : <strong className={styles.bad}>{runtimeDiff.data.changes.length} changed files</strong>
}

function BaselineRows({ baseline }: Pick<PluginsDashboardState, 'baseline'>) {
  if (baseline.status === 'loading') {
    return <p className={styles.localState}>Loading applied baseline…</p>
  }

  if (baseline.status === 'unavailable') {
    return <p className={styles.errorState}>Baseline unavailable: {baseline.message}</p>
  }

  if (baseline.data.status === 'notInitialized' || baseline.data.baseline === null) {
    return <p className={styles.localState}>Not initialized. No applied baseline exists yet.</p>
  }

  const { source, release, pluginsCommit, configsCommit, appliedAt } = baseline.data.baseline
  const sourceLabel = source === 'release' && release !== null
    ? `Release · release/${release}`
    : source === 'development'
      ? 'Development · develop'
      : source

  return (
    <>
      <div className={styles.labelRow}><span>Source</span><strong>{sourceLabel}</strong></div>
      <div className={styles.labelRow}><span>Plugins SHA</span><code>{pluginsCommit}</code></div>
      <div className={styles.labelRow}><span>Configs SHA</span><code>{configsCommit}</code></div>
      <div className={styles.labelRow}><span>Applied at</span><span>{formatAppliedAt(appliedAt)}</span></div>
    </>
  )
}

interface AppliedBaselinePanelProps {
  dashboard: PluginsDashboardState
  onOpenRuntimeDiff: () => void
  onRefreshRuntimeDiff: () => void
}

/** Displays the persisted applied baseline separately from Git and Runtime observations. */
export function AppliedBaselinePanel({
  dashboard,
  onOpenRuntimeDiff,
  onRefreshRuntimeDiff,
}: AppliedBaselinePanelProps) {
  const releasesText = dashboard.releases.status === 'available'
    ? dashboard.releases.data.releases.length === 0
      ? 'No known releases'
      : `${dashboard.releases.data.releases.length} known releases`
    : dashboard.releases.status === 'loading'
      ? 'Loading releases…'
      : 'Releases unavailable'

  return (
    <Panel aria-labelledby="applied-baseline-heading" className={styles.panel}>
      <div className={styles.panelHeader}>
        <h2 id="applied-baseline-heading">Applied Baseline</h2>
        <div className={styles.panelActions}>
          <button
            className={styles.tableCommand}
            disabled={dashboard.runtimeDiff.status !== 'available'}
            onClick={onOpenRuntimeDiff}
            type="button"
          >
            ● Runtime Diff{dashboard.runtimeDiff.status === 'available' && dashboard.runtimeDiff.data.changes.length > 0
              ? ` (${dashboard.runtimeDiff.data.changes.length})`
              : ''}
          </button>
          <button
            className={styles.tableCommand}
            disabled={dashboard.runtimeDiff.status === 'loading'}
            onClick={onRefreshRuntimeDiff}
            type="button"
          >
            {dashboard.runtimeDiff.status === 'loading' ? 'Refreshing…' : 'Refresh'}
          </button>
        </div>
      </div>
      <div className={styles.panelBody}>
        <BaselineRows baseline={dashboard.baseline} />
        <div className={styles.labelRow}>
          <span>Runtime vs Baseline</span>
          <RuntimeSummary runtimeDiff={dashboard.runtimeDiff} />
        </div>
        {dashboard.runtimeDiff.status === 'unavailable' && (
          <p className={styles.errorState} role="alert">
            Runtime Diff refresh failed: {dashboard.runtimeDiff.message}
          </p>
        )}
      </div>
      <footer className={styles.panelFooter}>
        <div>Release catalog: {releasesText}</div>
        {dashboard.releases.status === 'unavailable' && (
          <div className={styles.subtle}>{dashboard.releases.message}</div>
        )}
      </footer>
    </Panel>
  )
}
