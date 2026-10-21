import { afterEach, describe, expect, it, vi } from 'vitest'

import { getJson } from './get-json'
import { HttpError } from './request-json'

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('getJson', () => {
  it('uses GET and forwards an AbortSignal', async () => {
    const signal = new AbortController().signal
    const response = {
      ok: true,
      json: vi.fn().mockResolvedValue({ available: true }),
    } as unknown as Response
    const fetchMock = vi.fn().mockResolvedValue(response)
    vi.stubGlobal('fetch', fetchMock)

    await expect(getJson<{ available: boolean }>('/api/example', signal)).resolves.toEqual({
      available: true,
    })

    expect(fetchMock).toHaveBeenCalledWith('/api/example', {
      method: 'GET',
      signal,
    })
  })

  it('throws without parsing JSON when the response is unsuccessful', async () => {
    const json = vi.fn()
    const response = {
      ok: false,
      status: 503,
      statusText: 'Service Unavailable',
      json,
    } as unknown as Response
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(response))

    await expect(getJson('/api/example')).rejects.toThrow(
      'GET /api/example failed with HTTP 503 (Service Unavailable)',
    )

    expect(json).not.toHaveBeenCalled()
  })

  it('preserves a contract JSON error envelope for feature-specific handling', async () => {
    const body = {
      code: 'SourceNotAllowed',
      message: 'A Development baseline cannot switch to Production.',
      details: null,
    }
    const response = {
      ok: false,
      status: 409,
      statusText: 'Conflict',
      headers: { get: vi.fn().mockReturnValue('application/json; charset=utf-8') },
      json: vi.fn().mockResolvedValue(body),
    } as unknown as Response
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(response))

    await expect(getJson('/api/example')).rejects.toMatchObject({
      name: 'HttpError',
      responseBody: body,
      problemDetails: undefined,
    } satisfies Partial<HttpError>)
  })
})
