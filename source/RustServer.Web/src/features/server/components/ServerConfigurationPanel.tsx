import { useState } from 'react'

import type {
  ServerConfiguration,
  ServerConfigurationResponse,
  UpdateServerConfigurationRequest,
} from '../../../shared/api'
import type { ResourceState } from '../../control-plane/model'
import styles from '../../control-plane/ControlPlane.module.css'
import serverStyles from '../ServerPage.module.css'

interface ServerConfigurationPanelProps {
  configuration: ResourceState<ServerConfigurationResponse>
  mutationsBlocked: boolean
  pendingSave: boolean
  saveError: string | null
  saveSucceeded: boolean
  onSave: (request: UpdateServerConfigurationRequest) => void
  compact?: boolean
}

type NumberField =
  | 'maxPlayers'
  | 'tickRate'
  | 'saveInterval'
  | 'seed'
  | 'worldSize'

function configurationsEqual(
  first: ServerConfiguration,
  second: ServerConfiguration,
): boolean {
  return (
    first.hostname === second.hostname &&
    first.description === second.description &&
    first.maxPlayers === second.maxPlayers &&
    first.tickRate === second.tickRate &&
    first.saveInterval === second.saveInterval &&
    first.seed === second.seed &&
    first.worldSize === second.worldSize
  )
}

function formatValue(value: string | number): string {
  return value === '' ? 'Not set' : String(value)
}

function EffectiveValue({
  desired,
  effective,
  compact = false,
}: {
  desired: string | number
  effective: string | number
  compact?: boolean
}) {
  if (desired === effective) {
    return null
  }

  return <p className={compact ? serverStyles.configurationEffective : styles.effectiveValue}>Current: {formatValue(effective)}</p>
}

export function ServerConfigurationPanel({
  configuration,
  mutationsBlocked,
  pendingSave,
  saveError,
  saveSucceeded,
  onSave,
  compact = false,
}: ServerConfigurationPanelProps) {
  if (configuration.status === 'loading') {
    return (
      <article aria-label="Server configuration" className={compact ? serverStyles.configurationCompact : styles.panel}>
        <p className={styles.loadingState} role="status">
          Loading configuration.
        </p>
      </article>
    )
  }

  if (configuration.status === 'unavailable') {
    return (
      <article aria-label="Server configuration" className={compact ? serverStyles.configurationCompact : styles.panel}>
        {!compact && <h3>Desired configuration</h3>}
        <p className={styles.unavailableState} role="alert">
          Configuration unavailable.
        </p>
        {configuration.message && (
          <p className={styles.secondaryText}>{configuration.message}</p>
        )}
      </article>
    )
  }

  return (
    <AvailableServerConfigurationPanel
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

function AvailableServerConfigurationPanel({
  configuration,
  mutationsBlocked,
  pendingSave,
  saveError,
  saveSucceeded,
  onSave,
  compact = false,
}: Omit<ServerConfigurationPanelProps, 'configuration'> & {
  configuration: ServerConfigurationResponse
}) {
  const [draft, setDraft] = useState<UpdateServerConfigurationRequest>(
    configuration.desired,
  )

  const { desired, effective } = configuration
  const dirty = !configurationsEqual(draft, desired)
  const updateText = (field: 'hostname' | 'description', value: string) => {
    setDraft((current) => (current ? { ...current, [field]: value } : current))
  }
  const updateNumber = (field: NumberField, value: string) => {
    const parsed = Number(value)

    if (Number.isInteger(parsed)) {
      setDraft((current) => (current ? { ...current, [field]: parsed } : current))
    }
  }

  return (
    <article aria-label="Server configuration" className={compact ? serverStyles.configurationCompact : styles.panel}>
      {!compact && <h3>Desired configuration</h3>}
      <p className={compact ? serverStyles.configurationIntro : styles.secondaryText}>
        Saved settings apply on the next Start or Restart. Saving does not restart Rust.
      </p>

      {effective === null && (
        <p className={compact ? serverStyles.configurationWarning : styles.effectiveNotice}>
          Current configuration has not been confirmed by Control yet.
        </p>
      )}

      <form
        className={compact ? serverStyles.configurationForm : styles.configurationForm}
        onSubmit={(event) => {
          event.preventDefault()
          onSave(draft)
        }}
      >
        <label className={compact ? serverStyles.configurationField : styles.configurationField}>
          <span>Hostname</span>
          <input
            name="hostname"
            onChange={(event) => updateText('hostname', event.target.value)}
            type="text"
            value={draft.hostname}
          />
          {effective && (
            <EffectiveValue compact={compact} desired={draft.hostname} effective={effective.hostname} />
          )}
        </label>

        <label className={compact ? serverStyles.configurationField : styles.configurationField}>
          <span>Description</span>
          <textarea
            name="description"
            onChange={(event) => updateText('description', event.target.value)}
            rows={3}
            value={draft.description}
          />
          {effective && (
            <EffectiveValue
              desired={draft.description}
              effective={effective.description}
            />
          )}
        </label>

        <div className={compact ? serverStyles.configurationFormFields : styles.configurationFieldGrid}>
          <label className={compact ? serverStyles.configurationField : styles.configurationField}>
            <span>Max players</span>
            <input
              name="maxPlayers"
              onChange={(event) => updateNumber('maxPlayers', event.target.value)}
              type="number"
              value={draft.maxPlayers}
            />
            {effective && (
              <EffectiveValue
                desired={draft.maxPlayers}
                effective={effective.maxPlayers}
              />
            )}
          </label>

          <label className={compact ? serverStyles.configurationField : styles.configurationField}>
            <span>Tick rate</span>
            <input
              name="tickRate"
              onChange={(event) => updateNumber('tickRate', event.target.value)}
              type="number"
              value={draft.tickRate}
            />
            {effective && (
              <EffectiveValue compact={compact} desired={draft.tickRate} effective={effective.tickRate} />
            )}
          </label>

          <label className={compact ? serverStyles.configurationField : styles.configurationField}>
            <span>Save interval</span>
            <input
              name="saveInterval"
              onChange={(event) => updateNumber('saveInterval', event.target.value)}
              type="number"
              value={draft.saveInterval}
            />
            {effective && (
              <EffectiveValue
                desired={draft.saveInterval}
                effective={effective.saveInterval}
              />
            )}
          </label>

          <label className={compact ? serverStyles.configurationField : styles.configurationField}>
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

          <label className={compact ? serverStyles.configurationField : styles.configurationField}>
            <span>World size</span>
            <input
              name="worldSize"
              onChange={(event) => updateNumber('worldSize', event.target.value)}
              type="number"
              value={draft.worldSize}
            />
            {effective && (
              <EffectiveValue
                desired={draft.worldSize}
                effective={effective.worldSize}
              />
            )}
          </label>
        </div>

        {saveError && (
          <p className={compact ? serverStyles.runtimeWarning : styles.unavailableState} role="alert">
            {saveError}
          </p>
        )}
        {saveSucceeded && !dirty && (
          <p className={compact ? serverStyles.softwareGood : styles.successState} role="status">
            Desired configuration saved.
          </p>
        )}
        {dirty && <p className={compact ? serverStyles.configurationMessage : styles.secondaryText}>Unsaved changes.</p>}

        <div className={compact ? serverStyles.configurationActions : styles.formActions}>
          <button
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
