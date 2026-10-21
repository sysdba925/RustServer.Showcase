import { useState } from 'react'

import type {
  ServerConfiguration,
  ServerConfigurationResponse,
  UpdateServerConfigurationRequest,
} from '../../../shared/api'
import type { ResourceState } from '../../control-plane/model'
import styles from '../../control-plane/ControlPlane.module.css'
import worldStyles from '../WorldPage.module.css'

interface NextWorldConfigurationPanelProps {
  configuration: ResourceState<ServerConfigurationResponse>
  mutationsBlocked: boolean
  pendingSave: boolean
  saveError: string | null
  saveSucceeded: boolean
  onSave: (request: UpdateServerConfigurationRequest) => void
  compact?: boolean
}

function sameWorldSettings(
  first: ServerConfiguration,
  second: ServerConfiguration,
): boolean {
  return first.seed === second.seed && first.worldSize === second.worldSize
}

function EffectiveValue({
  desired,
  effective,
  compact = false,
}: {
  desired: number
  effective: number
  compact?: boolean
}) {
  return desired === effective ? null : (
    <p className={compact ? worldStyles.effective : styles.effectiveValue}>Current: {effective}</p>
  )
}

export function NextWorldConfigurationPanel({
  configuration,
  mutationsBlocked,
  pendingSave,
  saveError,
  saveSucceeded,
  onSave,
  compact = false,
}: NextWorldConfigurationPanelProps) {
  if (configuration.status === 'loading') {
    return (
      <article aria-label="Next world configuration" className={compact ? worldStyles.innerPanel : styles.panel}>
        <p className={compact ? worldStyles.muted : styles.loadingState} role="status">
          Loading next-world configuration.
        </p>
      </article>
    )
  }

  if (configuration.status === 'unavailable') {
    return (
      <article aria-label="Next world configuration" className={compact ? worldStyles.innerPanel : styles.panel}>
        {!compact && <h3>Next world</h3>}
        <p className={compact ? worldStyles.error : styles.unavailableState} role="alert">
          Configuration unavailable.
        </p>
        {configuration.message && (
          <p className={compact ? worldStyles.muted : styles.secondaryText}>{configuration.message}</p>
        )}
      </article>
    )
  }

  return (
    <AvailableNextWorldConfigurationPanel
      configuration={configuration.data}
      key={JSON.stringify(configuration.data.desired)}
      mutationsBlocked={mutationsBlocked}
      onSave={onSave}
      pendingSave={pendingSave}
      saveError={saveError}
      saveSucceeded={saveSucceeded}
      compact={compact}
    />
  )
}

function AvailableNextWorldConfigurationPanel({
  configuration,
  mutationsBlocked,
  pendingSave,
  saveError,
  saveSucceeded,
  onSave,
  compact = false,
}: Omit<NextWorldConfigurationPanelProps, 'configuration'> & {
  configuration: ServerConfigurationResponse
}) {
  const [draft, setDraft] = useState<UpdateServerConfigurationRequest>(
    configuration.desired,
  )

  const { desired, effective } = configuration
  const dirty = !sameWorldSettings(draft, desired)
  const updateNumber = (field: 'seed' | 'worldSize', value: string) => {
    const parsed = Number(value)

    if (Number.isInteger(parsed)) {
      setDraft((current) => (current ? { ...current, [field]: parsed } : current))
    }
  }

  return (
    <article aria-label="Next world configuration" className={compact ? worldStyles.innerPanel : styles.panel}>
      {!compact && <h3>Next world</h3>}
      <p className={compact ? worldStyles.note : styles.secondaryText}>
        These desired values are used for the next Rust process. Save them before
        wiping; saving does not wipe or restart Rust.
      </p>

      {effective === null && (
        <p className={compact ? worldStyles.notice : styles.effectiveNotice}>
          Current configuration has not been confirmed by Control yet.
        </p>
      )}

      <form
        className={compact ? worldStyles.configurationForm : styles.configurationForm}
        onSubmit={(event) => {
          event.preventDefault()
          onSave(draft)
        }}
      >
        <div className={compact ? worldStyles.fieldGrid : styles.configurationFieldGrid}>
          <label className={compact ? worldStyles.field : styles.configurationField}>
            <span>Seed</span>
            <input
              name="seed"
              onChange={(event) => updateNumber('seed', event.target.value)}
              type="number"
              value={draft.seed}
            />
            {effective && (
              <EffectiveValue compact={compact} desired={draft.seed} effective={effective.seed} />
            )}
          </label>

          <label className={compact ? worldStyles.field : styles.configurationField}>
            <span>World size</span>
            <input
              name="worldSize"
              onChange={(event) => updateNumber('worldSize', event.target.value)}
              type="number"
              value={draft.worldSize}
            />
            {effective && (
              <EffectiveValue
                compact={compact}
                desired={draft.worldSize}
                effective={effective.worldSize}
              />
            )}
          </label>
        </div>

        {saveError && (
          <p className={compact ? worldStyles.error : styles.unavailableState} role="alert">
            {saveError}
          </p>
        )}
        {saveSucceeded && !dirty && (
          <p className={compact ? worldStyles.success : styles.successState} role="status">
            Desired configuration saved.
          </p>
        )}
        {dirty && <p className={compact ? worldStyles.muted : styles.secondaryText}>Unsaved changes.</p>}

        <div className={compact ? worldStyles.actions : styles.formActions}>
          <button
            className={compact ? worldStyles.primary : undefined}
            disabled={!dirty || mutationsBlocked || pendingSave}
            type="submit"
          >
            {pendingSave ? 'Saving...' : 'Save configuration'}
          </button>
        </div>
      </form>
    </article>
  )
}
