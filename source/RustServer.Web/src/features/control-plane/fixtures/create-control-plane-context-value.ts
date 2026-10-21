import type { ControlPlaneContextValue } from '../ControlPlaneContext'
import { deriveActionAvailability } from '../model'
import {
  controlPlaneScenarios,
  noActiveStartup,
  noKnownOperation,
  serverConfiguration,
  startupSchema,
} from './control-plane-scenarios'

/** Creates a complete, side-effect-free context value for visual consumers. */
export function createControlPlaneContextValue(
  overrides: Partial<ControlPlaneContextValue> = {},
): ControlPlaneContextValue {
  const ready = controlPlaneScenarios.ready

  return {
    serverStatus: ready.serverStatus,
    configuration: { status: 'available', data: serverConfiguration },
    startupSchema: { status: 'available', data: startupSchema },
    startupSnapshot: { status: 'available', data: noActiveStartup },
    rustVersions: ready.rustVersions,
    oxideVersions: ready.oxideVersions,
    operationDetail: noKnownOperation,
    acceptedOperation: null,
    actionAvailability: deriveActionAvailability(ready),
    mutationsBlocked: false,
    pendingLifecycleAction: null,
    pendingUpdateTarget: null,
    pendingConfigurationSave: false,
    pendingWipe: false,
    lifecycleError: null,
    rustUpdateError: null,
    oxideUpdateError: null,
    configurationSaveError: null,
    configurationSaveSucceeded: false,
    wipeError: null,
    updateUnavailableReason: null,
    wipeUnavailableReason: null,
    start: () => {},
    stop: () => {},
    restart: () => {},
    requestRustUpdate: () => {},
    requestOxideUpdate: () => {},
    saveConfiguration: () => {},
    requestWipe: () => {},
    ...overrides,
  }
}
