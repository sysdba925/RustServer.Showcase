import { afterEach, describe, expect, it, vi } from 'vitest'

import type {
  OperationAcceptedResponse,
  OperationResponse,
  NetworkAccessResponse,
  ServerConfigurationResponse,
  ServerStatus,
  StartupSchemaResponse,
  StartupSnapshotResponse,
  UpdateNetworkAccessRequest,
  UpdateServerConfigurationRequest,
  WipePlanRequest,
} from './control-types'
import {
  getOperation,
  getNetworkAccess,
  getServerConfiguration,
  getServerStatus,
  getStartupSchema,
  getStartupSnapshot,
  requestOxideUpdate,
  requestRustUpdate,
  requestWipe,
  restartServer,
  startServer,
  stopServer,
  updateServerConfiguration,
  updateNetworkAccess,
} from './control-client'
import { HttpError } from './request-json'

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('getServerStatus', () => {
  it('requests the Control status endpoint and returns its JSON', async () => {
    const expectedStatus: ServerStatus = {
      isRunning: true,
      isReady: true,
    }
    const response = {
      ok: true,
      json: vi.fn().mockResolvedValue(expectedStatus),
    } as unknown as Response
    const fetchMock = vi.fn().mockResolvedValue(response)
    vi.stubGlobal('fetch', fetchMock)

    const status: ServerStatus = await getServerStatus()

    expect(fetchMock).toHaveBeenCalledWith('/control-api/api/server/status', {
      method: 'GET',
      signal: undefined,
    })
    expect(status).toEqual(expectedStatus)
  })

  it('throws for a non-successful Control response', async () => {
    const json = vi.fn()
    const response = {
      ok: false,
      status: 502,
      statusText: 'Bad Gateway',
      json,
    } as unknown as Response
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(response))

    await expect(getServerStatus()).rejects.toThrow(
      'GET /control-api/api/server/status failed with HTTP 502 (Bad Gateway)',
    )

    expect(json).not.toHaveBeenCalled()
  })
})

describe('Control server configuration clients', () => {
  const configuration: ServerConfigurationResponse = {
    desired: {
      hostname: 'Unit Test Server',
      description: 'A test configuration',
      maxPlayers: 50,
      tickRate: 30,
      saveInterval: 300,
      seed: 12345,
      worldSize: 4200,
    },
    effective: {
      hostname: 'Current Server',
      description: 'Running configuration',
      maxPlayers: 25,
      tickRate: 30,
      saveInterval: 300,
      seed: 67890,
      worldSize: 3500,
    },
  }

  it('gets desired and effective configuration', async () => {
    const signal = new AbortController().signal
    const response = {
      ok: true,
      json: vi.fn().mockResolvedValue(configuration),
    } as unknown as Response
    const fetchMock = vi.fn().mockResolvedValue(response)
    vi.stubGlobal('fetch', fetchMock)

    await expect(getServerConfiguration(signal)).resolves.toEqual(configuration)
    expect(fetchMock).toHaveBeenCalledWith('/control-api/api/server/configuration', {
      method: 'GET',
      signal,
    })
  })

  it('puts the complete desired configuration payload', async () => {
    const signal = new AbortController().signal
    const request: UpdateServerConfigurationRequest = configuration.desired
    const response = {
      ok: true,
      json: vi.fn().mockResolvedValue(configuration),
    } as unknown as Response
    const fetchMock = vi.fn().mockResolvedValue(response)
    vi.stubGlobal('fetch', fetchMock)

    await expect(updateServerConfiguration(request, signal)).resolves.toEqual(
      configuration,
    )
    expect(fetchMock).toHaveBeenCalledWith('/control-api/api/server/configuration', {
      method: 'PUT',
      signal,
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(request),
    })
  })
})

describe('Control network access clients', () => {
  const networkAccess: NetworkAccessResponse = {
    publicEnabled: true,
    maintenancePort: 50015,
  }

  it('gets persisted settings and puts the exact update request', async () => {
    const signal = new AbortController().signal
    const response = {
      ok: true,
      json: vi.fn().mockResolvedValue(networkAccess),
    } as unknown as Response
    const fetchMock = vi.fn().mockResolvedValue(response)
    vi.stubGlobal('fetch', fetchMock)
    const request: UpdateNetworkAccessRequest = {
      publicEnabled: false,
      maintenancePort: 50016,
    }

    await expect(getNetworkAccess(signal)).resolves.toEqual(networkAccess)
    await expect(updateNetworkAccess(request, signal)).resolves.toEqual(networkAccess)

    expect(fetchMock).toHaveBeenNthCalledWith(
      1,
      '/control-api/api/server/network-access',
      { method: 'GET', signal },
    )
    expect(fetchMock).toHaveBeenNthCalledWith(
      2,
      '/control-api/api/server/network-access',
      {
        method: 'PUT',
        signal,
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(request),
      },
    )
  })
})

describe('Startup Tracking clients', () => {
  const schema: StartupSchemaResponse = {
    version: 1,
    milestones: [
      { id: 'engine', label: 'Unity Engine' },
      { id: 'mono', label: 'Mono Runtime' },
    ],
  }
  const snapshot: StartupSnapshotResponse = {
    sessionId: 'stp_current-run',
    started: false,
    milestones: ['reached', 'pending'],
  }

  it('gets the fixed startup schema and forwards its AbortSignal', async () => {
    const signal = new AbortController().signal
    const response = {
      ok: true,
      json: vi.fn().mockResolvedValue(schema),
    } as unknown as Response
    const fetchMock = vi.fn().mockResolvedValue(response)
    vi.stubGlobal('fetch', fetchMock)

    await expect(getStartupSchema(signal)).resolves.toEqual(schema)
    expect(fetchMock).toHaveBeenCalledWith('/control-api/api/server/startup/schema', {
      method: 'GET',
      signal,
    })
  })

  it('gets the complete startup snapshot and forwards its AbortSignal', async () => {
    const signal = new AbortController().signal
    const response = {
      ok: true,
      json: vi.fn().mockResolvedValue(snapshot),
    } as unknown as Response
    const fetchMock = vi.fn().mockResolvedValue(response)
    vi.stubGlobal('fetch', fetchMock)

    await expect(getStartupSnapshot(signal)).resolves.toEqual(snapshot)
    expect(fetchMock).toHaveBeenCalledWith('/control-api/api/server/startup', {
      method: 'GET',
      signal,
    })
  })
})

async function captureHttpError(
  request: () => Promise<unknown>,
): Promise<HttpError> {
  try {
    await request()
  } catch (error) {
    expect(error).toBeInstanceOf(HttpError)

    return error as HttpError
  }

  throw new Error('Expected the request to reject with HttpError.')
}

describe('Control lifecycle clients', () => {
  const expectedStatus: ServerStatus = {
    isRunning: true,
    isReady: true,
  }

  it.each([
    ['startServer', '/control-api/api/server/start', startServer],
    ['stopServer', '/control-api/api/server/stop', stopServer],
    ['restartServer', '/control-api/api/server/restart', restartServer],
  ] as const)('%s sends a bodyless POST request and returns ServerStatus', async (
    _name,
    url,
    request,
  ) => {
    const signal = new AbortController().signal
    const response = {
      ok: true,
      json: vi.fn().mockResolvedValue(expectedStatus),
    } as unknown as Response
    const fetchMock = vi.fn().mockResolvedValue(response)
    vi.stubGlobal('fetch', fetchMock)

    const status = await request(signal)

    expect(status).toEqual(expectedStatus)
    expect(fetchMock).toHaveBeenCalledWith(url, {
      method: 'POST',
      signal,
    })
    expect(fetchMock.mock.calls[0][1]).not.toHaveProperty('body')
  })

  it('preserves ProblemDetails from a lifecycle conflict', async () => {
    const problemDetails = {
      title: 'Server mutation in progress',
      detail: 'A server-mutating update is pending or running.',
      status: 409,
    }
    const response = {
      ok: false,
      status: 409,
      statusText: 'Conflict',
      headers: {
        get: vi.fn().mockReturnValue('application/problem+json; charset=utf-8'),
      },
      json: vi.fn().mockResolvedValue(problemDetails),
    } as unknown as Response
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(response))

    const error = await captureHttpError(() => startServer())

    expect(error.status).toBe(409)
    expect(error.statusText).toBe('Conflict')
    expect(error.problemDetails?.title).toBe(problemDetails.title)
    expect(error.problemDetails?.detail).toBe(problemDetails.detail)
  })

  it('preserves an HTTP failure when its ProblemDetails body is unusable', async () => {
    const response = {
      ok: false,
      status: 409,
      statusText: 'Conflict',
      headers: {
        get: vi.fn().mockReturnValue('application/problem+json'),
      },
      json: vi.fn().mockResolvedValue({ title: 409 }),
    } as unknown as Response
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(response))

    const error = await captureHttpError(() => stopServer())

    expect(error.method).toBe('POST')
    expect(error.url).toBe('/control-api/api/server/stop')
    expect(error.status).toBe(409)
    expect(error.statusText).toBe('Conflict')
    expect(error.problemDetails).toBeUndefined()
  })
})

describe('Control update clients', () => {
  const accepted: OperationAcceptedResponse = {
    id: 'f1ccbe0c-a923-4a38-8f3d-9c0f58b4c0d4',
    status: 'Pending',
  }

  it.each([
    ['requestRustUpdate', '/control-api/api/server/update', requestRustUpdate],
    ['requestOxideUpdate', '/control-api/api/updates/oxide', requestOxideUpdate],
  ] as const)('%s sends a bodyless POST and forwards its AbortSignal', async (
    _name,
    url,
    request,
  ) => {
    const signal = new AbortController().signal
    const response = {
      ok: true,
      json: vi.fn().mockResolvedValue(accepted),
    } as unknown as Response
    const fetchMock = vi.fn().mockResolvedValue(response)
    vi.stubGlobal('fetch', fetchMock)

    await expect(request(signal)).resolves.toEqual(accepted)
    expect(fetchMock).toHaveBeenCalledWith(url, {
      method: 'POST',
      signal,
    })
    expect(fetchMock.mock.calls[0][1]).not.toHaveProperty('body')
  })

  it('gets one safely encoded operation ID and forwards its AbortSignal', async () => {
    const operation: OperationResponse = {
      id: accepted.id,
      type: 'RustUpdate',
      status: 'Running',
      createdAt: '2026-09-29T08:30:00Z',
      wipeResult: null,
    }
    const signal = new AbortController().signal
    const response = {
      ok: true,
      json: vi.fn().mockResolvedValue(operation),
    } as unknown as Response
    const fetchMock = vi.fn().mockResolvedValue(response)
    vi.stubGlobal('fetch', fetchMock)

    await expect(getOperation('operation/id with spaces', signal)).resolves.toEqual(
      operation,
    )
    expect(fetchMock).toHaveBeenCalledWith(
      '/control-api/api/operations/operation%2Fid%20with%20spaces',
      { method: 'GET', signal },
    )
  })

  it('posts an exact semantic wipe plan and forwards its AbortSignal', async () => {
    const signal = new AbortController().signal
    const plan: WipePlanRequest = {
      world: 'delete',
      blueprints: 'keep',
      pluginData: [],
    }
    const response = {
      ok: true,
      json: vi.fn().mockResolvedValue(accepted),
    } as unknown as Response
    const fetchMock = vi.fn().mockResolvedValue(response)
    vi.stubGlobal('fetch', fetchMock)

    await expect(requestWipe(plan, signal)).resolves.toEqual(accepted)
    expect(fetchMock).toHaveBeenCalledWith('/control-api/api/server/wipe', {
      method: 'POST',
      signal,
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(plan),
    })
  })

  it('keeps the path-free wipe result shape in an operation response', () => {
    const operation: OperationResponse = {
      id: accepted.id,
      type: 'Wipe',
      status: 'Succeeded',
      createdAt: '2026-09-29T08:30:00Z',
      wipeResult: {
        succeeded: true,
        targets: [
          {
            target: 'world',
            action: 'delete',
            outcome: 'deleted',
            affectedItems: 2,
            error: null,
          },
        ],
      },
    }

    expect(operation.wipeResult?.targets[0]).toEqual({
      target: 'world',
      action: 'delete',
      outcome: 'deleted',
      affectedItems: 2,
      error: null,
    })
  })

  it('preserves ProblemDetails from an update conflict', async () => {
    const response = {
      ok: false,
      status: 409,
      statusText: 'Conflict',
      headers: {
        get: vi.fn().mockReturnValue('application/problem+json'),
      },
      json: vi.fn().mockResolvedValue({
        title: 'Update unavailable',
        detail: 'The server is no longer stopped.',
      }),
    } as unknown as Response
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(response))

    const error = await captureHttpError(() => requestRustUpdate())

    expect(error.status).toBe(409)
    expect(error.problemDetails?.detail).toBe('The server is no longer stopped.')
  })

  it.each([500, 502])(
    'keeps update HTTP %i as a structured failure',
    async (status) => {
      const response = {
        ok: false,
        status,
        statusText: 'Service error',
        headers: {
          get: vi.fn().mockReturnValue('application/problem+json'),
        },
        json: vi.fn().mockResolvedValue({ title: 'Update failed' }),
      } as unknown as Response
      vi.stubGlobal('fetch', vi.fn().mockResolvedValue(response))

      const error = await captureHttpError(() => requestOxideUpdate())

      expect(error.status).toBe(status)
      expect(error.problemDetails?.title).toBe('Update failed')
    },
  )

  it('keeps a bodyless operation 404 as an HttpError', async () => {
    const response = {
      ok: false,
      status: 404,
      statusText: 'Not Found',
    } as unknown as Response
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(response))

    const error = await captureHttpError(() => getOperation(accepted.id))

    expect(error.status).toBe(404)
    expect(error.problemDetails).toBeUndefined()
  })
})
