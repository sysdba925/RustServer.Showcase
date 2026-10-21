import { useEffect, useRef, useState } from 'react'

import {
  getNetworkAccess,
  HttpError,
  updateNetworkAccess,
  type NetworkAccessResponse,
  type UpdateNetworkAccessRequest,
} from '../../../shared/api'
import serverStyles from '../ServerPage.module.css'

const defaultDraft = {
  publicEnabled: true,
  maintenancePort: '50015',
}

interface NetworkAccessDraft {
  publicEnabled: boolean
  maintenancePort: string
}

export interface NetworkAccessApi {
  getNetworkAccess: (signal?: AbortSignal) => Promise<NetworkAccessResponse>
  updateNetworkAccess: (
    request: UpdateNetworkAccessRequest,
    signal?: AbortSignal,
  ) => Promise<NetworkAccessResponse>
}

const controlNetworkAccessApi: NetworkAccessApi = {
  getNetworkAccess,
  updateNetworkAccess,
}

function createDraft(response: NetworkAccessResponse): NetworkAccessDraft {
  return {
    publicEnabled: response.publicEnabled,
    maintenancePort: String(response.maintenancePort),
  }
}

function getErrorMessage(error: unknown): string {
  if (error instanceof HttpError) {
    const detail = error.problemDetails?.detail?.trim()

    if (detail) {
      const code = typeof error.problemDetails?.code === 'string' ? error.problemDetails.code.trim() : ''
      return code ? `${detail} (Code: ${code})` : detail
    }

    const title = error.problemDetails?.title?.trim()

    if (title) {
      return title
    }
  }

  return error instanceof Error && error.message !== ''
    ? error.message
    : 'Request failed.'
}

function isValidMaintenancePort(value: string): boolean {
  const port = Number(value)
  return /^\d+$/.test(value.trim()) &&
    Number.isInteger(port) &&
    port >= 1 && port <= 65535 &&
    port !== 28015 && port !== 28017
}

function createRequest(draft: NetworkAccessDraft): UpdateNetworkAccessRequest {
  return {
    publicEnabled: draft.publicEnabled,
    maintenancePort: Number(draft.maintenancePort),
  }
}

/** Loads and edits the Control API's persisted NetworkAccess settings. */
export function NetworkAccessPanel({
  api = controlNetworkAccessApi,
}: {
  api?: NetworkAccessApi
}) {
  const [networkAccess, setNetworkAccess] = useState<NetworkAccessResponse | null>(null)
  const [draft, setDraft] = useState<NetworkAccessDraft>(defaultDraft)
  const [isLoading, setIsLoading] = useState(true)
  const [loadError, setLoadError] = useState<string | null>(null)
  const [isOpen, setIsOpen] = useState(false)
  const [isSaving, setIsSaving] = useState(false)
  const [saveError, setSaveError] = useState<string | null>(null)
  const saveControllerRef = useRef<AbortController | null>(null)
  const controlRef = useRef<HTMLDivElement | null>(null)
  const triggerRef = useRef<HTMLButtonElement | null>(null)

  useEffect(() => {
    const controller = new AbortController()

    async function loadNetworkAccess(): Promise<void> {
      try {
        const response = await api.getNetworkAccess(controller.signal)
        if (controller.signal.aborted) {
          return
        }
        setNetworkAccess(response)
        setDraft(createDraft(response))
        setLoadError(null)
      } catch (error: unknown) {
        if (!controller.signal.aborted) {
          setLoadError(getErrorMessage(error))
        }
      } finally {
        if (!controller.signal.aborted) {
          setIsLoading(false)
        }
      }
    }

    void loadNetworkAccess()

    return () => {
      controller.abort()
      saveControllerRef.current?.abort()
    }
  }, [api])

  // Dismissing the editor discards its draft. Only Save can change the server.
  useEffect(() => {
    if (!isOpen || isSaving) {
      return
    }

    function dismiss(): void {
      if (networkAccess !== null) {
        setDraft(createDraft(networkAccess))
      }
      setSaveError(null)
      setIsOpen(false)
    }

    function onOutsideClick(event: MouseEvent): void {
      if (event.target instanceof Node && !controlRef.current?.contains(event.target)) {
        dismiss()
      }
    }

    function onEscape(event: KeyboardEvent): void {
      if (event.key === 'Escape') {
        dismiss()
        triggerRef.current?.focus()
      }
    }

    document.addEventListener('mousedown', onOutsideClick)
    document.addEventListener('keydown', onEscape)
    return () => {
      document.removeEventListener('mousedown', onOutsideClick)
      document.removeEventListener('keydown', onEscape)
    }
  }, [isOpen, isSaving, networkAccess])

  const maintenancePortIsValid = isValidMaintenancePort(draft.maintenancePort)
  const hasUnsavedChanges = networkAccess !== null && (
    draft.publicEnabled !== networkAccess.publicEnabled ||
    draft.maintenancePort !== String(networkAccess.maintenancePort)
  )
  const maintenancePortChanged = networkAccess !== null &&
    draft.maintenancePort !== String(networkAccess.maintenancePort)
  const controlsDisabled = isLoading || loadError !== null || isSaving

  function closeEditor(): void {
    if (isSaving) {
      return
    }
    if (networkAccess !== null) {
      setDraft(createDraft(networkAccess))
    }
    setSaveError(null)
    setIsOpen(false)
  }

  async function saveNetworkAccess(): Promise<void> {
    if (networkAccess === null || !hasUnsavedChanges || !maintenancePortIsValid || isSaving) {
      return
    }

    saveControllerRef.current?.abort()
    const controller = new AbortController()
    saveControllerRef.current = controller
    setIsSaving(true)
    setSaveError(null)

    try {
      const response = await api.updateNetworkAccess(createRequest(draft), controller.signal)
      if (controller.signal.aborted || saveControllerRef.current !== controller) {
        return
      }
      setNetworkAccess(response)
      setDraft(createDraft(response))
      setIsOpen(false)
    } catch (error: unknown) {
      if (!controller.signal.aborted && saveControllerRef.current === controller) {
        setSaveError(getErrorMessage(error))
      }
    } finally {
      if (saveControllerRef.current === controller) {
        saveControllerRef.current = null
        setIsSaving(false)
      }
    }
  }

  const publicState = networkAccess === null ? 'unknown' : networkAccess.publicEnabled ? 'on' : 'off'
  const currentLabel = networkAccess === null
    ? isLoading ? 'Network Access loading' : 'Network Access unavailable'
    : 'Public ' + (networkAccess.publicEnabled ? 'ON' : 'OFF') + ' · ' + networkAccess.maintenancePort
  const accessibleLabel = networkAccess === null
    ? currentLabel
    : 'Network access: Public ' + (networkAccess.publicEnabled ? 'ON' : 'OFF') +
      ', maintenance port ' + networkAccess.maintenancePort

  return (
    <div className={serverStyles.networkAccessControl} ref={controlRef}>
      <button
        aria-controls="network-access-settings"
        aria-expanded={isOpen}
        aria-label={accessibleLabel}
        className={serverStyles.networkAccessTrigger}
        data-state={publicState}
        disabled={isSaving}
        onClick={() => {
          if (isOpen) {
            closeEditor()
          } else {
            setIsOpen(true)
          }
        }}
        ref={triggerRef}
        type="button"
      >
        <svg aria-hidden="true" className={serverStyles.networkAccessGlobe} viewBox="0 0 24 24">
          <circle cx="12" cy="12" r="9" />
          <path d="M3 12h18M12 3c-3 3-3 15 0 18M12 3c3 3 3 15 0 18" />
        </svg>
        <span>{currentLabel}</span>
        <svg aria-hidden="true" className={serverStyles.networkAccessChevron} viewBox="0 0 16 16">
          <path d="m3 6 5 5 5-5" />
        </svg>
      </button>

      {isOpen && (
        <div
          aria-label="Network Access settings"
          className={serverStyles.networkAccessPopover}
          id="network-access-settings"
          role="group"
        >
          <form
            className={serverStyles.networkAccessForm}
            onSubmit={(event) => {
              event.preventDefault()
              void saveNetworkAccess()
            }}
          >
            <div className={serverStyles.networkAccessHeader}>
              <strong>Network Access</strong>
              <span
                aria-label="The public game ports are 28015 and 28017. The maintenance port connects to the same server."
                className={serverStyles.networkAccessInfo}
                title="Public ON opens UDP 28015/28017; Public OFF blocks both. Maintenance uses a separate UDP port."
              >ⓘ</span>
            </div>

            {isLoading && <p className={serverStyles.configurationMessage} role="status">Loading network access.</p>}
            {loadError && (
              <p className={serverStyles.runtimeWarning} role="alert">
                Network access is unavailable. {loadError}
              </p>
            )}

            <label className={serverStyles.networkAccessToggle}>
              <span className={serverStyles.networkAccessToggleLabel}>Public Access</span>
              <input
                aria-label="Public Access"
                checked={draft.publicEnabled}
                disabled={controlsDisabled}
                onChange={(event) => {
                  setDraft((current) => ({ ...current, publicEnabled: event.target.checked }))
                }}
                role="switch"
                type="checkbox"
              />
              <span aria-hidden="true" className={serverStyles.networkAccessSwitch} data-state={draft.publicEnabled ? 'on' : 'off'}>
                <span className={serverStyles.networkAccessThumb} />
              </span>
              <span className={serverStyles.networkAccessToggleState} data-state={draft.publicEnabled ? 'on' : 'off'}>
                {draft.publicEnabled ? 'Public ON' : 'Public OFF'}
              </span>
            </label>

            <label className={serverStyles.networkAccessField}>
              <span>Maintenance Port</span>
              <input
                disabled={controlsDisabled}
                inputMode="numeric"
                max="65535"
                min="1"
                name="maintenancePort"
                onChange={(event) => {
                  setDraft((current) => ({ ...current, maintenancePort: event.target.value }))
                }}
                step="1"
                type="number"
                value={draft.maintenancePort}
              />
            </label>

            {maintenancePortChanged && (
              <p className={serverStyles.networkAccessWarning}>
                Changing the Maintenance Port may disconnect the current game connection using the old port.
              </p>
            )}
            {!maintenancePortIsValid && !controlsDisabled && (
              <p className={serverStyles.networkAccessWarning} role="alert">
                Enter a UDP port from 1 to 65535, except 28015 and 28017.
              </p>
            )}
            {saveError && (
              <p className={serverStyles.runtimeWarning} role="alert">
                Network access was not saved. {saveError}
              </p>
            )}
            <p className={serverStyles.networkAccessHint}>
              Save stores the desired mode. Public ports remain blocked until Rust is Ready.
            </p>
            <div className={serverStyles.networkAccessActions}>
              <button
                className={serverStyles.networkAccessSave}
                disabled={controlsDisabled || !hasUnsavedChanges || !maintenancePortIsValid}
                type="submit"
              >
                {isSaving ? 'Saving...' : 'Save'}
              </button>
              <button className={serverStyles.networkAccessCancel} disabled={isSaving} onClick={closeEditor} type="button">
                Cancel
              </button>
            </div>
          </form>
        </div>
      )}
    </div>
  )
}
