import { afterEach, expect, it, vi } from 'vitest'

import { exportPluginCatalog, previewPluginCatalogRestore } from './plugin-catalog-client'
import { requestJson, requestNoContent } from './request-json'

afterEach(() => {
  vi.unstubAllGlobals()
})

it('redirects once on 401 across API request paths and preserves other errors', async () => {
  const replace = vi.fn()
  vi.stubGlobal('window', { location: { replace } })
  const fetchMock = vi.fn()
  vi.stubGlobal('fetch', fetchMock)

  const requests = [
    () => requestJson('GET', '/json'),
    () => requestNoContent('DELETE', '/no-content'),
    () => exportPluginCatalog(),
    () => previewPluginCatalogRestore(new File(['zip'], 'backup.zip')),
  ]

  for (const status of [403, 404, 409, 500, 503]) {
    fetchMock.mockImplementation(async () => new Response(JSON.stringify({ title: 'Failure', status }), {
      status,
      headers: { 'Content-Type': 'application/problem+json' },
    }))
    for (const request of requests) {
      await expect(request()).rejects.toMatchObject({ name: 'HttpError', status })
    }
    expect(replace).not.toHaveBeenCalled()
  }

  const networkError = new TypeError('Network error')
  fetchMock.mockRejectedValue(networkError)
  for (const request of requests) {
    await expect(request()).rejects.toBe(networkError)
  }
  expect(replace).not.toHaveBeenCalled()

  fetchMock.mockImplementation(async () => new Response(null, { status: 401 }))
  const results = await Promise.allSettled(requests.map((request) => request()))
  for (const result of results) {
    expect(result).toMatchObject({ status: 'rejected', reason: { name: 'HttpError', status: 401 } })
  }
  expect(replace).toHaveBeenCalledOnce()
  expect(replace).toHaveBeenCalledWith('/')
})
