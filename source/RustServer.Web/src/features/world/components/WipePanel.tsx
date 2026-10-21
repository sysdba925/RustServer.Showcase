import { useState } from 'react'

import type { WipeAction, WipePlanRequest } from '../../../shared/api'
import {
  classifyOperationStatus,
  formatOperationStatus,
  type Operation,
  type ResourceState,
} from '../../control-plane/model'
import styles from '../../control-plane/ControlPlane.module.css'
import worldStyles from '../WorldPage.module.css'

interface WipePanelProps {
  mutationsBlocked: boolean
  operationDetail: ResourceState<Operation | null>
  pendingWipe: boolean
  unavailableReason: string | null
  wipeError: string | null
  onWipe: (plan: WipePlanRequest) => void
  compact?: boolean
}

type RustTargetWipeAction = Extract<WipeAction, 'keep' | 'delete'>

function WipeTargetPolicy({
  label,
  name,
  value,
  onChange,
  compact = false,
}: {
  label: string
  name: string
  value: RustTargetWipeAction
  onChange: (value: RustTargetWipeAction) => void
  compact?: boolean
}) {
  return (
    <fieldset className={compact ? worldStyles.wipePolicy : styles.wipePolicy}>
      <legend>{label}</legend>
      <label>
        <input
          checked={value === 'keep'}
          name={name}
          onChange={() => onChange('keep')}
          type="radio"
          value="keep"
        />
        Keep
      </label>
      <label>
        <input
          checked={value === 'delete'}
          name={name}
          onChange={() => onChange('delete')}
          type="radio"
          value="delete"
        />
        Delete
      </label>
    </fieldset>
  )
}

function WipeResult({ operation, compact = false }: { operation: Operation; compact?: boolean }) {
  if (operation.wipeResult == null) {
    return null
  }

  const result = operation.wipeResult

  return (
    <section aria-label="Wipe result" className={compact ? worldStyles.sectionDivider : styles.wipeResult}>
      <div className={compact ? worldStyles.headerTools : styles.panelHeader}>
        <h4 className={compact ? worldStyles.subHeading : undefined}>{compact ? 'Latest wipe result' : 'Wipe result'}</h4>
        <span
          className={compact
            ? worldStyles.status
            : `${styles.statusBadge} ${result.succeeded ? styles.statusReady : styles.statusUnavailable}`}
          data-tone={result.succeeded ? 'ok' : 'off'}
        >
          {result.succeeded ? 'Succeeded' : 'Failed'}
        </span>
      </div>
      <dl className={compact ? worldStyles.detailRows : styles.wipeResultList}>
        {result.targets.map((target) => (
          <div key={target.target}>
            <dt>{target.target}</dt>
            <dd>
              Action: {target.action}; outcome: {target.outcome}; affected items:{' '}
              {target.affectedItems}
              {target.error != null && <><br />Error: {target.error}</>}
            </dd>
          </div>
        ))}
      </dl>
    </section>
  )
}

export function WipePanel({
  mutationsBlocked,
  operationDetail,
  pendingWipe,
  unavailableReason,
  wipeError,
  onWipe,
  compact = false,
}: WipePanelProps) {
  const [world, setWorld] = useState<RustTargetWipeAction>('keep')
  const [blueprints, setBlueprints] = useState<RustTargetWipeAction>('keep')
  const [confirmationOpen, setConfirmationOpen] = useState(false)
  const operation =
    operationDetail.status === 'available' ? operationDetail.data : null
  const wipeInProgress =
    pendingWipe ||
    (operation?.type === 'Wipe' &&
      operation.wipeResult == null &&
      classifyOperationStatus(operation.status) === 'active')
  const disabled = mutationsBlocked || unavailableReason !== null || wipeInProgress
  const plan: WipePlanRequest = { world, blueprints, pluginData: [] }

  return (
    <article aria-label="Wipe plan" className={compact ? worldStyles.innerPanel : styles.panel}>
      {!compact && <h3>Wipe plan</h3>}
      <p className={compact ? worldStyles.note : styles.secondaryText}>
        Choose each Rust target independently. Wipe never stops, starts, or
        restarts the server.
      </p>

      <div className={compact ? worldStyles.wipePolicies : styles.wipePolicies}>
        <WipeTargetPolicy
          compact={compact}
          label="World"
          name="world-wipe-policy"
          onChange={setWorld}
          value={world}
        />
        <WipeTargetPolicy
          compact={compact}
          label="Blueprints"
          name="blueprints-wipe-policy"
          onChange={setBlueprints}
          value={blueprints}
        />
      </div>

      {unavailableReason && (
        <p className={compact ? worldStyles.notice : styles.secondaryText}>{unavailableReason}</p>
      )}
      {wipeError && (
        <p className={compact ? worldStyles.error : styles.unavailableState} role="alert">
          {wipeError}
        </p>
      )}
      {wipeInProgress && operation && (
        <p className={compact ? worldStyles.muted : styles.secondaryText} role="status">
          Wipe is {formatOperationStatus(operation.status)}.
        </p>
      )}

      {!confirmationOpen ? (
        <div className={compact ? worldStyles.actions : styles.formActions}>
          <button
            className={compact ? worldStyles.danger : styles.destructiveButton}
            disabled={disabled}
            onClick={() => setConfirmationOpen(true)}
            type="button"
          >
            Execute wipe
          </button>
        </div>
      ) : (
        <div className={compact ? worldStyles.modalBackdrop : undefined}>
          <section
            aria-labelledby="wipe-confirmation-heading"
            aria-modal={compact || undefined}
            className={compact ? worldStyles.modalDialog : styles.wipeConfirmation}
            role="dialog"
          >
          <h4 id="wipe-confirmation-heading">Confirm wipe plan</h4>
          <dl className={compact ? worldStyles.detailRows : styles.wipeConfirmationList}>
            <div>
              <dt>World</dt>
              <dd>{world === 'delete' ? 'Delete' : 'Keep'}</dd>
            </div>
            <div>
              <dt>Blueprints</dt>
              <dd>{blueprints === 'delete' ? 'Delete' : 'Keep'}</dd>
            </div>
          </dl>
          <div className={compact ? worldStyles.actions : styles.formActions}>
            <button onClick={() => setConfirmationOpen(false)} type="button">
              Cancel
            </button>
            <button
              className={compact ? worldStyles.danger : styles.destructiveButton}
              disabled={disabled}
              onClick={() => {
                setConfirmationOpen(false)
                onWipe(plan)
              }}
              type="button"
            >
              {pendingWipe ? 'Executing wipe...' : 'Execute wipe'}
            </button>
          </div>
          </section>
        </div>
      )}

      {compact && (operation?.type !== 'Wipe' || operation.wipeResult == null) && (
        <section className={worldStyles.sectionDivider}>
          <h4 className={worldStyles.subHeading}>Latest wipe result</h4>
          <p className={worldStyles.muted}>No known operation.</p>
        </section>
      )}
      {operation && <WipeResult compact={compact} operation={operation} />}
    </article>
  )
}
