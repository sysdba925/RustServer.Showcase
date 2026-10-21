import { createContext, useContext } from 'react'

import type {
  AcceptedOperationContext,
  ActionAvailability,
  LifecycleAction,
  Operation,
  OxideVersionInfo,
  ResourceState,
  RustVersionInfo,
  ServerStatus,
  StartupSchemaResponse,
  StartupSnapshotResponse,
  UpdateTarget,
} from './model'
import type {
  ServerConfigurationResponse,
  UpdateServerConfigurationRequest,
  WipePlanRequest,
} from '../../shared/api'

export interface ControlPlaneContextValue {
  serverStatus: ResourceState<ServerStatus>
  configuration: ResourceState<ServerConfigurationResponse>
  startupSchema: ResourceState<StartupSchemaResponse>
  startupSnapshot: ResourceState<StartupSnapshotResponse>
  rustVersions: ResourceState<RustVersionInfo>
  oxideVersions: ResourceState<OxideVersionInfo>
  operationDetail: ResourceState<Operation | null>
  acceptedOperation: AcceptedOperationContext | null
  actionAvailability: ActionAvailability
  mutationsBlocked: boolean
  pendingLifecycleAction: LifecycleAction | null
  pendingUpdateTarget: UpdateTarget | null
  pendingConfigurationSave: boolean
  pendingWipe: boolean
  lifecycleError: string | null
  rustUpdateError: string | null
  oxideUpdateError: string | null
  configurationSaveError: string | null
  configurationSaveSucceeded: boolean
  wipeError: string | null
  updateUnavailableReason: string | null
  wipeUnavailableReason: string | null
  start: () => void
  stop: () => void
  restart: () => void
  requestRustUpdate: () => void
  requestOxideUpdate: () => void
  saveConfiguration: (request: UpdateServerConfigurationRequest) => void
  requestWipe: (plan: WipePlanRequest) => void
}

export const ControlPlaneContext =
  createContext<ControlPlaneContextValue | null>(null)

export function useControlPlane(): ControlPlaneContextValue {
  const value = useContext(ControlPlaneContext)

  if (value === null) {
    throw new Error('useControlPlane must be used within ControlPlaneProvider.')
  }

  return value
}
