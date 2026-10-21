import { useCallback, useEffect, useRef, useState } from 'react'

import {
  changeEnvironment as requestEnvironmentChange,
  createRelease,
  getEnvironment,
  getProductionDeployment,
  getReleases,
  getStagingRuntimeChanges,
  type EnvironmentResponse,
  HttpError,
  type ProductionDeploymentResponse,
  type ReleaseResponse,
  type ServiceEnvironment,
  type StagingRuntimeChangesResponse,
} from '../../shared/api'
import { getOperatorErrorMessage, type ResourceState } from './model'

export type PluginsDeployedSource = 'development' | 'release'

export interface DeploymentSourceContext {
  source: PluginsDeployedSource | null
  releaseId: string | null
}

interface ScopedSourceResource {
  environment: ServiceEnvironment
  resource: ResourceState<DeploymentSourceContext>
}

function normalizeSource(source: string): PluginsDeployedSource | null {
  if (source === 'development' || source === 'release') {
    return source
  }

  return null
}

export function usePluginsEnvironment() {
  const [environment, setEnvironment] = useState<ResourceState<EnvironmentResponse>>({
    status: 'loading',
  })
  const requestVersionRef = useRef(0)

  useEffect(() => {
    const controller = new AbortController()
    const requestVersion = ++requestVersionRef.current

    void getEnvironment(controller.signal)
      .then((data) => {
        if (!controller.signal.aborted && requestVersionRef.current === requestVersion) {
          setEnvironment({ status: 'available', data })
        }
      })
      .catch((error: unknown) => {
        if (!controller.signal.aborted && requestVersionRef.current === requestVersion) {
          setEnvironment({ status: 'unavailable', message: getOperatorErrorMessage(error) })
        }
      })

    return () => controller.abort()
  }, [])

  const change = useCallback(async (nextEnvironment: ServiceEnvironment) => {
    const requestVersion = ++requestVersionRef.current
    const data = await requestEnvironmentChange({ environment: nextEnvironment })

    if (requestVersionRef.current === requestVersion) {
      setEnvironment({ status: 'available', data })
    }

    return data
  }, [])

  return { environment, change }
}

/**
 * Establishes the deployed Source only from a published baseline response for
 * the current logical Environment. A missing or unsupported baseline remains
 * unknown rather than being inferred from local selection state.
 */
export function useDeploymentSource(environment: ResourceState<EnvironmentResponse>) {
  const [scopedResource, setScopedResource] = useState<ScopedSourceResource | null>(null)
  const currentEnvironment = environment.status === 'available'
    ? environment.data.environment
    : null

  useEffect(() => {
    if (currentEnvironment === null) {
      return undefined
    }

    const controller = new AbortController()

    if (currentEnvironment === 'staging') {
      void getStagingRuntimeChanges(controller.signal)
        .then((data: StagingRuntimeChangesResponse) => {
          if (!controller.signal.aborted) {
            setScopedResource({
              environment: currentEnvironment,
              resource: {
                status: 'available',
                data: { source: normalizeSource(data.source), releaseId: null },
              },
            })
          }
        })
        .catch((error: unknown) => {
          if (!controller.signal.aborted) {
            setScopedResource({
              environment: currentEnvironment,
              resource: { status: 'unavailable', message: getOperatorErrorMessage(error) },
            })
          }
        })
    } else {
      void getProductionDeployment(controller.signal)
        .then((data) => {
          if (!controller.signal.aborted) {
            setScopedResource({
              environment: currentEnvironment,
              resource: {
                status: 'available',
                data: {
                  source: normalizeSource(data.source),
                  releaseId: data.releaseId,
                },
              },
            })
          }
        })
        .catch((error: unknown) => {
          if (controller.signal.aborted) {
            return
          }

          if (error instanceof HttpError && error.status === 404) {
            setScopedResource({
              environment: currentEnvironment,
              resource: {
                status: 'available',
                data: { source: null, releaseId: null },
              },
            })
          } else {
            setScopedResource({
              environment: currentEnvironment,
              resource: { status: 'unavailable', message: getOperatorErrorMessage(error) },
            })
          }
        })
    }

    return () => controller.abort()
  }, [currentEnvironment])

  const source: ResourceState<DeploymentSourceContext> = currentEnvironment === null
    ? { status: 'unavailable', message: 'Environment is unavailable.' }
    : scopedResource?.environment === currentEnvironment
      ? scopedResource.resource
      : { status: 'loading' }

  const recordDeployment = useCallback(
    (deploymentEnvironment: ServiceEnvironment, sourceName: string, releaseId: string | null) => {
      setScopedResource({
        environment: deploymentEnvironment,
        resource: {
          status: 'available',
          data: { source: normalizeSource(sourceName), releaseId },
        },
      })
    },
    [],
  )

  return { source, recordDeployment }
}

export function useReleases() {
  const [releases, setReleases] = useState<ResourceState<ReleaseResponse[]>>({
    status: 'loading',
  })
  const [creating, setCreating] = useState(false)
  const [createError, setCreateError] = useState<string | null>(null)
  const [createdRelease, setCreatedRelease] = useState<ReleaseResponse | null>(null)
  const controllerRef = useRef<AbortController | null>(null)

  const refresh = useCallback(async () => {
    controllerRef.current?.abort()
    const controller = new AbortController()
    controllerRef.current = controller
    setReleases({ status: 'loading' })

    try {
      const data = await getReleases(controller.signal)
      if (!controller.signal.aborted && controllerRef.current === controller) {
        setReleases({ status: 'available', data })
      }
    } catch (error) {
      if (!controller.signal.aborted && controllerRef.current === controller) {
        setReleases({ status: 'unavailable', message: getOperatorErrorMessage(error) })
      }
    }
  }, [])

  useEffect(() => {
    void refresh()
    return () => controllerRef.current?.abort()
  }, [refresh])

  const create = useCallback(async () => {
    setCreating(true)
    setCreateError(null)
    setCreatedRelease(null)
    try {
      const created = await createRelease()
      setCreatedRelease(created)
      await refresh()
    } catch (error) {
      setCreateError(getOperatorErrorMessage(error))
    } finally {
      setCreating(false)
    }
  }, [refresh])

  return { releases, refresh, create, creating, createError, createdRelease }
}

export function useProductionDeployment(enabled = true) {
  const [deployment, setDeployment] = useState<
    ResourceState<ProductionDeploymentResponse | null>
  >({ status: 'loading' })
  const controllerRef = useRef<AbortController | null>(null)

  const refresh = useCallback(async () => {
    controllerRef.current?.abort()
    const controller = new AbortController()
    controllerRef.current = controller
    setDeployment({ status: 'loading' })

    try {
      const data = await getProductionDeployment(controller.signal)
      if (!controller.signal.aborted && controllerRef.current === controller) {
        setDeployment({ status: 'available', data })
      }
    } catch (error) {
      if (controller.signal.aborted || controllerRef.current !== controller) {
        return
      }

      if (error instanceof HttpError && error.status === 404) {
        setDeployment({ status: 'available', data: null })
      } else {
        setDeployment({ status: 'unavailable', message: getOperatorErrorMessage(error) })
      }
    }
  }, [])

  useEffect(() => {
    if (!enabled) {
      return undefined
    }

    void refresh()
    return () => controllerRef.current?.abort()
  }, [enabled, refresh])

  return { deployment, refresh }
}
