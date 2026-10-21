import type {
  GetBaselineResponse,
  GetEnvironmentResponse,
  GetGitStatusResponse,
  GetReleasesResponse,
  GetRuntimeDiffResponse,
} from '../../../shared/api'

export type PluginsResource<T> =
  | { status: 'loading' }
  | { status: 'available'; data: T }
  | { status: 'unavailable'; message: string }

export interface PluginsDashboardState {
  environment: PluginsResource<GetEnvironmentResponse>
  baseline: PluginsResource<GetBaselineResponse>
  gitStatus: PluginsResource<GetGitStatusResponse>
  runtimeDiff: PluginsResource<GetRuntimeDiffResponse>
  releases: PluginsResource<GetReleasesResponse>
}

export const loadingPluginsDashboard: PluginsDashboardState = {
  environment: { status: 'loading' },
  baseline: { status: 'loading' },
  gitStatus: { status: 'loading' },
  runtimeDiff: { status: 'loading' },
  releases: { status: 'loading' },
}
