import type { ReleaseResponse, SourcePreviewResponse } from '../../../shared/api'
import { formatSource, type SourceDraft } from '../model/source'

import { PluginsDialog } from './PluginsDialog'
import styles from '../PluginsPage.module.css'

interface SourceSelectionDialogProps {
  environment: string
  draft: SourceDraft
  releases: ReleaseResponse[]
  previewError: string | null
  previewing: boolean
  hasValidSelection: boolean
  onCancel: () => void
  onChange: (draft: SourceDraft) => void
  onPreview: () => void
}

function releaseAvailability(release: ReleaseResponse): string {
  const missing = [
    release.pluginsHead === null ? 'Plugins' : null,
    release.configsHead === null ? 'Configs' : null,
  ].filter((repository): repository is string => repository !== null)

  return missing.length === 0
    ? 'Complete in Plugins and Configs'
    : `Unavailable: missing ${missing.join(' and ')} release branch${missing.length === 1 ? '' : 'es'}.`
}

/** Selects a draft target; it deliberately does not change the applied baseline. */
export function SourceSelectionDialog({
  environment,
  draft,
  releases,
  previewError,
  previewing,
  hasValidSelection,
  onCancel,
  onChange,
  onPreview,
}: SourceSelectionDialogProps) {
  const isProduction = environment === 'production'
  const firstCompleteRelease = releases.find((release) => release.status === 'complete')

  return (
    <PluginsDialog
      footer={(
        <>
          <button className={styles.commandButton} disabled={previewing} onClick={onCancel} type="button">
            Cancel
          </button>
          <button className={`${styles.commandButton} ${styles.primaryCommand}`} disabled={previewing || !hasValidSelection} onClick={onPreview} type="button">
            {previewing ? 'Requesting preview…' : 'Preview Source'}
          </button>
        </>
      )}
      onClose={previewing ? undefined : onCancel}
      title="Switch Source"
    >
      <p className={styles.dialogHint}>
        Select a target Source and review its actual Runtime preview. Selection is
        only a draft: no files, Baseline, or Environment change until Apply succeeds.
      </p>
      <div className={styles.modeGroup}>
        <button
          aria-pressed={draft.kind === 'development'}
          className={`${styles.modeButton} ${draft.kind === 'development' ? styles.modeButtonSelected : ''}`}
          disabled={isProduction || previewing}
          onClick={() => onChange({ kind: 'development' })}
          type="button"
        >
          <strong>Development</strong>
          <span>develop{isProduction ? ' · unavailable in Production' : ''}</span>
        </button>
        <button
          aria-pressed={draft.kind === 'release'}
          className={`${styles.modeButton} ${draft.kind === 'release' ? styles.modeButtonSelected : ''}`}
          disabled={releases.length === 0 || previewing}
          onClick={() => onChange({ kind: 'release', version: firstCompleteRelease?.version ?? '' })}
          type="button"
        >
          <strong>Release</strong>
          <span>Inspect releases below; only complete versions can be selected</span>
        </button>
      </div>

      {draft.kind === 'release' && (
        <section className={styles.releaseSection}>
          <h3>Available Releases</h3>
          <div className={styles.releaseList}>
            {releases.map((release) => {
              const selected = release.version === draft.version
              const complete = release.status === 'complete'
              const availability = releaseAvailability(release)

              return (
                <button
                  aria-pressed={selected}
                  className={`${styles.releaseItem} ${selected ? styles.releaseItemSelected : ''}`}
                  disabled={!complete || previewing}
                  key={release.branch}
                  onClick={() => onChange({ kind: 'release', version: release.version })}
                  title={availability}
                  type="button"
                >
                  <span className={styles.releaseItemHeader}>
                    <strong>release/{release.version}</strong>
                    <span className={complete ? styles.releaseStatusComplete : styles.releaseStatusIncomplete}>
                      {complete ? 'Complete' : 'Incomplete'}
                    </span>
                  </span>
                  <span className={styles.releaseRepositories}>
                    <span className={release.pluginsHead === null ? styles.releaseRepositoryMissing : styles.releaseRepositoryPresent}>
                      {release.pluginsHead === null ? '✕ Plugins — branch missing' : '✓ Plugins — branch found'}
                    </span>
                    <span className={release.configsHead === null ? styles.releaseRepositoryMissing : styles.releaseRepositoryPresent}>
                      {release.configsHead === null ? '✕ Configs — branch missing' : '✓ Configs — branch found'}
                    </span>
                  </span>
                  {!complete && <span className={styles.releaseUnavailable}>{availability}</span>}
                  {complete && selected && <span className={styles.releaseSelected}>Selected target</span>}
                </button>
              )
            })}
          </div>
          {releases.length === 0 && <p className={styles.localState}>No known Releases. Fetch Git to refresh remote-tracking refs.</p>}
        </section>
      )}
      {previewError !== null && <p className={styles.errorState} role="alert">{previewError}</p>}
    </PluginsDialog>
  )
}

interface SourcePreviewDialogProps {
  preview: SourcePreviewResponse | null
  applying: boolean
  onBack: () => void
  onCancel: () => void
  onConfirmApply: () => void
}

function previewBaseline(preview: SourcePreviewResponse): string {
  if (preview.baseline === null) {
    return 'No applied baseline yet'
  }

  return formatSource(preview.baseline.source, preview.baseline.release)
}

function changeClass(change: string): string {
  switch (change) {
    case 'added': return styles.changeAdded
    case 'deleted': return styles.changeDeleted
    default: return styles.changeModified
  }
}

/** Renders only the preview details published by the backend. */
export function SourcePreviewDialog({
  preview,
  applying,
  onBack,
  onCancel,
  onConfirmApply,
}: SourcePreviewDialogProps) {
  const loading = preview === null

  return (
    <PluginsDialog
      footer={loading ? (
        <button className={styles.commandButton} disabled type="button">Requesting preview…</button>
      ) : (
        <>
          <button className={styles.commandButton} disabled={applying} onClick={onBack} type="button">Back</button>
          <button className={styles.commandButton} disabled={applying} onClick={onCancel} type="button">Cancel</button>
          <button className={`${styles.commandButton} ${styles.primaryCommand}`} disabled={applying} onClick={onConfirmApply} type="button">
            Confirm Apply
          </button>
        </>
      )}
      onClose={loading || applying ? undefined : onCancel}
      title="Source Preview · Review before Apply"
    >
      {loading ? (
        <p className={styles.localState} role="status">Requesting the current source preview…</p>
      ) : (
        <>
          <p className={styles.dialogHint}>
            This preview compares the selected Git Source with managed Runtime files.
            Apply performs a fresh fetch and validation, so this result is informational.
          </p>
          <div className={styles.previewGrid}>
            <section className={styles.previewCard}>
              <h3>Applied Baseline</h3>
              <p>{previewBaseline(preview)}</p>
              {preview.baseline !== null && (
                <>
                  <code>Plugins {preview.baseline.pluginsCommit}</code>
                  <code>Configs {preview.baseline.configsCommit}</code>
                </>
              )}
            </section>
            <section className={styles.previewCard}>
              <h3>Target Git Source</h3>
              <p>{formatSource(preview.targetSource.type, preview.targetSource.release)}</p>
              <code>Plugins {preview.targetGit.pluginsHead}</code>
              <code>Configs {preview.targetGit.configsHead}</code>
            </section>
          </div>
          {preview.currentSource === null && preview.currentSourceDiagnostic !== null && (
            <p className={styles.dialogHint}>
              Current workspace source is unavailable: Plugins {preview.currentSourceDiagnostic.pluginsBranch ?? 'unknown'}, Configs {preview.currentSourceDiagnostic.configsBranch ?? 'unknown'}.
            </p>
          )}
          <div className={styles.previewSummary}>
            <span className={styles.good}>+ {preview.counts.added} Added</span>
            <span className={styles.caution}>~ {preview.counts.modified} Modified</span>
            <span className={styles.bad}>− {preview.counts.deleted} Deleted</span>
          </div>
          <div className={styles.previewFiles}>
            {preview.changes.map((change) => (
              <div className={changeClass(change.change)} key={`${change.repository}:${change.path}`}>
                <strong>{change.change}</strong> {change.repository} / {change.path}
              </div>
            ))}
            {preview.changes.length === 0 && <p className={styles.localState}>No managed file changes are reported.</p>}
          </div>
          <p className={styles.productionNotice}>
            Apply updates managed Oxide files and the Baseline, even while Rust is running.
            Plugin behavior after the file change is not validated; Git and Runtime safety are rechecked before Apply.
          </p>
        </>
      )}
    </PluginsDialog>
  )
}
