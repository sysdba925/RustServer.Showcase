import { useCallback, useEffect, useRef, useState } from 'react'

import { pluginsApi, type PluginsApi } from '../api'
import {
  loadingPluginsDashboard,
  type PluginsDashboardState,
  type PluginsResource,
} from '../model/plugins-dashboard'

export type PluginsDashboardResource = keyof PluginsDashboardState

interface PluginsDashboardRefreshFailure {
  resource: PluginsDashboardResource
  message: string
}

function resourceLabel(resource: PluginsDashboardResource): string {
  switch (resource) {
    case 'environment': return 'Environment'
    case 'baseline': return 'Baseline'
    case 'gitStatus': return 'Git status'
    case 'runtimeDiff': return 'Runtime diff'
    case 'releases': return 'Releases'
  }
}

/**
 * A completed dashboard refresh can have more than one independently
 * unavailable resource. Keep that detail for operation workflows, while the
 * dashboard itself exposes each resource's state directly.
 */
export class PluginsDashboardRefreshError extends Error {
  readonly failures: readonly PluginsDashboardRefreshFailure[]

  constructor(failures: readonly PluginsDashboardRefreshFailure[]) {
    super(failures.map(({ resource, message }) => `${resourceLabel(resource)}: ${message}`).join(' '))
    this.name = 'PluginsDashboardRefreshError'
    this.failures = failures
  }
}

const allDashboardResources: PluginsDashboardResource[] = [
  'environment',
  'baseline',
  'gitStatus',
  'runtimeDiff',
  'releases',
]

function getErrorMessage(error: unknown): string {
  return error instanceof Error && error.message !== ''
    ? error.message
    : 'Request failed.'
}

interface UsePluginsDashboardOptions {
  api?: PluginsApi
  initialDashboard?: PluginsDashboardState
}

interface PluginsDashboardController {
  dashboard: PluginsDashboardState
  refresh: (resources?: readonly PluginsDashboardResource[]) => Promise<void>
}

/**
 * Owns independently refreshable Plugins read resources. Operation workflows
 * use `refresh` after a report reaches a terminal state instead of predicting
 * the observable server state locally.
 */
export function usePluginsDashboard({
  api = pluginsApi,
  initialDashboard,
}: UsePluginsDashboardOptions = {}): PluginsDashboardController {
  const [dashboard, setDashboard] = useState<PluginsDashboardState>(
    initialDashboard ?? loadingPluginsDashboard,
  )
  const controllersRef = useRef(new Map<PluginsDashboardResource, AbortController>())

  const refreshResource = useCallback(async (
    resource: PluginsDashboardResource,
  ): Promise<PluginsDashboardRefreshFailure | null> => {
    controllersRef.current.get(resource)?.abort()

    const controller = new AbortController()
    controllersRef.current.set(resource, controller)

    function setResource<T>(value: PluginsResource<T>): void {
      if (!controller.signal.aborted && controllersRef.current.get(resource) === controller) {
        setDashboard((current) => ({ ...current, [resource]: value }))
      }
    }

    try {
      setResource({ status: 'loading' })

      switch (resource) {
        case 'environment':
          setResource({ status: 'available', data: await api.getEnvironment(controller.signal) })
          break
        case 'baseline':
          setResource({ status: 'available', data: await api.getBaseline(controller.signal) })
          break
        case 'gitStatus':
          setResource({ status: 'available', data: await api.getGitStatus(controller.signal) })
          break
        case 'runtimeDiff':
          setResource({ status: 'available', data: await api.getRuntimeDiff(controller.signal) })
          break
        case 'releases':
          setResource({ status: 'available', data: await api.getReleases(controller.signal) })
          break
      }
    } catch (error: unknown) {
      if (controller.signal.aborted || controllersRef.current.get(resource) !== controller) {
        return null
      }

      const message = getErrorMessage(error)
      setResource({ status: 'unavailable', message })
      return { resource, message }
    } finally {
      if (controllersRef.current.get(resource) === controller) {
        controllersRef.current.delete(resource)
      }
    }

    return null
  }, [api])

  const refresh = useCallback(async (
    resources: readonly PluginsDashboardResource[] = allDashboardResources,
  ): Promise<void> => {
    const results = await Promise.all(resources.map((resource) => refreshResource(resource)))
    const failures = results.filter((result): result is PluginsDashboardRefreshFailure => result !== null)

    if (failures.length > 0) {
      throw new PluginsDashboardRefreshError(failures)
    }
  }, [refreshResource])

  useEffect(() => {
    const controllers = controllersRef.current

    if (initialDashboard === undefined) {
      // Initial resource failures are represented by their individual cards.
      void refresh().catch(() => undefined)
    }

    return () => {
      for (const controller of controllers.values()) {
        controller.abort()
      }
      controllers.clear()
    }
  }, [initialDashboard, refresh])

  useEffect(() => {
    if (initialDashboard !== undefined) {
      // Stories and focused tests may deliberately replace their static snapshot.
      // oxlint-disable-next-line react/set-state-in-effect
      setDashboard(initialDashboard)
    }
  }, [initialDashboard])

  return { dashboard, refresh }
}
