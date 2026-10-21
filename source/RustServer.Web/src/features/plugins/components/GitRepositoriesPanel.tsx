import { Panel } from '../../../shared/ui/Panel'
import type { GetGitRepositoryStatusResponse } from '../../../shared/api'
import type { PluginsDashboardState } from '../model/plugins-dashboard'
import styles from '../PluginsPage.module.css'

function relationPresentation(relation: string): { label: string; tone: 'good' | 'caution' | 'bad' } {
  switch (relation) {
    case 'equal':
      return { label: '✓ Equal', tone: 'good' }
    case 'ahead':
      return { label: '↑ Ahead', tone: 'caution' }
    case 'behind':
      return { label: '↓ Behind', tone: 'caution' }
    case 'diverged':
      return { label: '⚠ Diverged', tone: 'bad' }
    default:
      return { label: '? Unknown', tone: 'bad' }
  }
}

function relationAction(repository: GetGitRepositoryStatusResponse): {
  label: string
  available: boolean
} {
  if (!repository.configured) {
    return { label: '⚠ Unavailable', available: false }
  }

  if (repository.workspace !== 'clean') {
    return { label: '⚠ Dirty', available: false }
  }

  switch (repository.relation) {
    case 'ahead':
      return { label: '⇡ Push', available: true }
    case 'behind':
      return { label: '⇣ Fast-forward', available: true }
    case 'diverged':
      return { label: '⇄ Merge + Push', available: true }
    case 'equal':
      return { label: '✓ Synced', available: false }
    default:
      return { label: '⚠ Unavailable', available: false }
  }
}

function formatTimestamp(value: string | null): string {
  if (value === null) {
    return 'Last Fetch: Unknown'
  }

  const parsed = new Date(value)
  return Number.isNaN(parsed.getTime())
    ? 'Last Fetch: Invalid timestamp'
    : `Last Fetch: ${new Intl.DateTimeFormat('en-GB', {
        dateStyle: 'medium',
        timeStyle: 'short',
        timeZone: 'UTC',
      }).format(parsed)} UTC`
}

function RepositoryRow({
  label,
  repository,
  actionsBlocked,
  onSynchronize,
}: {
  label: string
  repository: GetGitRepositoryStatusResponse
  actionsBlocked: boolean
  onSynchronize: () => void
}) {
  const relation = relationPresentation(repository.relation)
  const action = relationAction(repository)

  return (
    <tr>
      <td><strong>{label}</strong></td>
      <td className={styles.monospace}>{repository.localBranch ?? 'Unavailable'}</td>
      <td className={styles[relation.tone]}>{relation.label}</td>
      <td>
        <button
          aria-label={`${label} ${action.label}`}
          className={styles.tableCommand}
          disabled={actionsBlocked || !action.available}
          onClick={onSynchronize}
          title={repository.diagnostic ?? undefined}
          type="button"
        >
          {action.label}
        </button>
        {repository.diagnostic !== null && <div className={styles.subtle}>{repository.diagnostic}</div>}
      </td>
    </tr>
  )
}

interface GitRepositoriesPanelProps {
  gitStatus: PluginsDashboardState['gitStatus']
  actionsBlocked: boolean
  onFetch: () => void
  onSynchronize: (repository: 'plugins' | 'configs') => void
}

/** Presents the distinct local and known-remote states of the two managed repositories. */
export function GitRepositoriesPanel({
  gitStatus,
  actionsBlocked,
  onFetch,
  onSynchronize,
}: GitRepositoriesPanelProps) {
  const canFetch = gitStatus.status === 'available' &&
    gitStatus.data.plugins.configured &&
    gitStatus.data.configs.configured &&
    !actionsBlocked

  return (
    <Panel aria-labelledby="git-repositories-heading" className={styles.panel}>
      <div className={styles.panelHeader}>
        <h2 id="git-repositories-heading">Git Repositories</h2>
        <button className={styles.tableCommand} disabled={!canFetch} onClick={onFetch} type="button">↻ Fetch Git</button>
      </div>
      <div className={styles.panelBody}>
        {gitStatus.status === 'loading' && <p className={styles.localState}>Loading Git status…</p>}
        {gitStatus.status === 'unavailable' && (
          <p className={styles.errorState}>Git status unavailable: {gitStatus.message}</p>
        )}
        {gitStatus.status === 'available' && (
          <table className={styles.repositoryTable}>
            <thead>
              <tr>
                <th>Repository</th>
                <th>Local Branch</th>
                <th>Local ↔ Remote</th>
                <th>Sync</th>
              </tr>
            </thead>
            <tbody>
              <RepositoryRow
                actionsBlocked={actionsBlocked}
                label="Plugins"
                onSynchronize={() => onSynchronize('plugins')}
                repository={gitStatus.data.plugins}
              />
              <RepositoryRow
                actionsBlocked={actionsBlocked}
                label="Configs"
                onSynchronize={() => onSynchronize('configs')}
                repository={gitStatus.data.configs}
              />
            </tbody>
          </table>
        )}
      </div>
      <footer className={styles.panelFooter}>
        {gitStatus.status === 'available' ? (
          <>
            <div>{formatTimestamp(gitStatus.data.lastFetchAt)}</div>
            <div className={styles.subtle}>Git status: {gitStatus.data.gitStatus}</div>
          </>
        ) : (
          <div>Last Fetch: {gitStatus.status === 'loading' ? 'Loading' : 'Unavailable'}</div>
        )}
      </footer>
    </Panel>
  )
}
