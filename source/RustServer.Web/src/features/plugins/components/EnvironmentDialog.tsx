import { useState } from 'react'

import { PluginsDialog } from './PluginsDialog'
import styles from '../PluginsPage.module.css'

interface EnvironmentDialogProps {
  currentEnvironment: string
  submitting: boolean
  error: string | null
  onCancel: () => void
  onConfirm: (environment: string) => void
}

/** Explicitly confirms a logical Environment change without suggesting deployment. */
export function EnvironmentDialog({
  currentEnvironment,
  submitting,
  error,
  onCancel,
  onConfirm,
}: EnvironmentDialogProps) {
  const [targetEnvironment, setTargetEnvironment] = useState(currentEnvironment)
  const isProduction = targetEnvironment === 'production'
  const changed = targetEnvironment !== currentEnvironment

  return (
    <PluginsDialog
      footer={(
        <>
          <button className={styles.commandButton} disabled={submitting} onClick={onCancel} type="button">
            Cancel
          </button>
          <button
            className={`${styles.commandButton} ${styles.primaryCommand}`}
            disabled={!changed || submitting}
            onClick={() => onConfirm(targetEnvironment)}
            type="button"
          >
            {submitting ? 'Changing environment…' : 'Confirm environment'}
          </button>
        </>
      )}
      onClose={submitting ? undefined : onCancel}
      title="Change Environment"
    >
      <p className={styles.dialogHint}>
        Choose the logical Plugins environment. This does not deploy files or start,
        stop, or restart Rust.
      </p>
      <div className={styles.modeGroup}>
        <button
          aria-pressed={targetEnvironment === 'staging'}
          className={`${styles.modeButton} ${targetEnvironment === 'staging' ? styles.modeButtonSelected : ''}`}
          disabled={submitting}
          onClick={() => setTargetEnvironment('staging')}
          type="button"
        >
          <strong>Staging</strong>
          <span>Development or Release</span>
        </button>
        <button
          aria-pressed={isProduction}
          className={`${styles.modeButton} ${isProduction ? styles.modeButtonSelected : ''}`}
          disabled={submitting}
          onClick={() => setTargetEnvironment('production')}
          type="button"
        >
          <strong>Production</strong>
          <span>Release only</span>
        </button>
      </div>
      <p className={isProduction ? styles.productionNotice : styles.dialogTarget}>
        Target environment: <strong>{targetEnvironment === 'production' ? 'Production' : 'Staging'}</strong>
      </p>
      {isProduction && (
        <p className={styles.dialogHint}>
          Production requires an applied Release baseline. The backend validates this
          requirement and will reject an unsafe transition.
        </p>
      )}
      {error !== null && <p className={styles.errorState} role="alert">{error}</p>}
    </PluginsDialog>
  )
}
