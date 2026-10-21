import { afterEach, describe, expect, it, vi } from 'vitest'

import {
  applyPluginsSource,
  commitPluginsRuntime,
  fetchPluginsGit,
  getPluginsBaseline,
  getPluginsEnvironment,
  getPluginsGitStatus,
  getPluginsOperation,
  getPluginsOperations,
  getPluginsReleases,
  getPluginsRuntimeDiffContent,
  getPluginsRuntimeDiff,
  previewPluginsSource,
  revertAllPluginsRuntime,
  revertPluginsRuntime,
  syncPluginsGit,
  updatePluginsEnvironment,
} from './plugins-client'

afterEach(() => vi.unstubAllGlobals())

describe('Plugins read clients', () => {
  it.each([
    ['getPluginsEnvironment', '/plugins-api/api/environment', getPluginsEnvironment],
    ['getPluginsBaseline', '/plugins-api/api/baseline', getPluginsBaseline],
    ['getPluginsGitStatus', '/plugins-api/api/git/status', getPluginsGitStatus],
    ['getPluginsRuntimeDiff', '/plugins-api/api/runtime/diff', getPluginsRuntimeDiff],
    ['getPluginsReleases', '/plugins-api/api/releases', getPluginsReleases],
  ] as const)('%s issues the published read-only GET endpoint', async (_name, url, request) => {
    const signal = new AbortController().signal
    const response = { ok: true, json: vi.fn().mockResolvedValue({}) } as unknown as Response
    const fetchMock = vi.fn().mockResolvedValue(response)
    vi.stubGlobal('fetch', fetchMock)

    await request(signal)

    expect(fetchMock).toHaveBeenCalledWith(url, { method: 'GET', signal })
  })

  it('sends the published Environment mutation payload', async () => {
    const signal = new AbortController().signal
    const response = { ok: true, json: vi.fn().mockResolvedValue({ environment: 'production' }) } as unknown as Response
    const fetchMock = vi.fn().mockResolvedValue(response)
    vi.stubGlobal('fetch', fetchMock)

    await updatePluginsEnvironment({ environment: 'production' }, signal)

    expect(fetchMock).toHaveBeenCalledWith('/plugins-api/api/environment', {
      method: 'PUT',
      signal,
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ environment: 'production' }),
    })
  })

  it('uses the contract Git Fetch and one-repository Sync requests', async () => {
    const signal = new AbortController().signal
    const response = { ok: true, json: vi.fn().mockResolvedValue({ operationId: 'op-git' }) } as unknown as Response
    const fetchMock = vi.fn().mockResolvedValue(response)
    vi.stubGlobal('fetch', fetchMock)

    await fetchPluginsGit(signal)
    await syncPluginsGit({ repository: 'configs' }, signal)

    expect(fetchMock).toHaveBeenNthCalledWith(1, '/plugins-api/api/git/fetch', {
      method: 'POST', signal,
    })
    expect(fetchMock).toHaveBeenNthCalledWith(2, '/plugins-api/api/git/sync', {
      method: 'POST',
      signal,
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ repository: 'configs' }),
    })
  })

  it('sends Source Preview and Apply target requests without inventing a branch field', async () => {
    const signal = new AbortController().signal
    const response = { ok: true, json: vi.fn().mockResolvedValue({}) } as unknown as Response
    const fetchMock = vi.fn().mockResolvedValue(response)
    vi.stubGlobal('fetch', fetchMock)

    await previewPluginsSource({ source: 'development' }, signal)
    await applyPluginsSource({ source: 'release', release: '3.2.0' }, signal)

    expect(fetchMock).toHaveBeenNthCalledWith(1, '/plugins-api/api/source/preview', {
      method: 'POST',
      signal,
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ source: 'development' }),
    })
    expect(fetchMock).toHaveBeenNthCalledWith(2, '/plugins-api/api/source/apply', {
      method: 'POST',
      signal,
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ source: 'release', release: '3.2.0' }),
    })
  })

  it('encodes the published Runtime Diff content query', async () => {
    const signal = new AbortController().signal
    const response = { ok: true, json: vi.fn().mockResolvedValue({}) } as unknown as Response
    const fetchMock = vi.fn().mockResolvedValue(response)
    vi.stubGlobal('fetch', fetchMock)

    await getPluginsRuntimeDiffContent('configs', 'oxide/config/A file.json', signal)

    expect(fetchMock).toHaveBeenCalledWith(
      '/plugins-api/api/runtime/diff/content?repository=configs&path=oxide%2Fconfig%2FA+file.json',
      { method: 'GET', signal },
    )
  })

  it('uses published Runtime Commit and selective Revert payloads', async () => {
    const signal = new AbortController().signal
    const response = { ok: true, json: vi.fn().mockResolvedValue({}) } as unknown as Response
    const fetchMock = vi.fn().mockResolvedValue(response)
    vi.stubGlobal('fetch', fetchMock)
    const changes = [{ repository: 'plugins', path: 'oxide/plugins/Example.cs' }]

    await commitPluginsRuntime({ message: 'Keep the runtime update', changes }, signal)
    await revertPluginsRuntime({ changes }, signal)

    expect(fetchMock).toHaveBeenNthCalledWith(1, '/plugins-api/api/runtime/commit', {
      method: 'POST',
      signal,
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ message: 'Keep the runtime update', changes }),
    })
    expect(fetchMock).toHaveBeenNthCalledWith(2, '/plugins-api/api/runtime/revert', {
      method: 'POST',
      signal,
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ changes }),
    })
  })

  it('keeps Revert all bodyless as required by the legacy contract form', async () => {
    const signal = new AbortController().signal
    const response = { ok: true, json: vi.fn().mockResolvedValue({}) } as unknown as Response
    const fetchMock = vi.fn().mockResolvedValue(response)
    vi.stubGlobal('fetch', fetchMock)

    await revertAllPluginsRuntime(signal)

    expect(fetchMock).toHaveBeenCalledWith('/plugins-api/api/runtime/revert', {
      method: 'POST',
      signal,
    })
  })

  it('encodes a known operation id in the published report lookup path', async () => {
    const signal = new AbortController().signal
    const response = { ok: true, json: vi.fn().mockResolvedValue({}) } as unknown as Response
    const fetchMock = vi.fn().mockResolvedValue(response)
    vi.stubGlobal('fetch', fetchMock)

    await getPluginsOperation('op/a', signal)

    expect(fetchMock).toHaveBeenCalledWith('/plugins-api/api/operations/op%2Fa', {
      method: 'GET', signal,
    })
  })

  it('sends the published operation history limit and opaque cursor', async () => {
    const signal = new AbortController().signal
    const response = { ok: true, json: vi.fn().mockResolvedValue({}) } as unknown as Response
    const fetchMock = vi.fn().mockResolvedValue(response)
    vi.stubGlobal('fetch', fetchMock)

    await getPluginsOperations(200, '2026-10-10T10:15:00Z|op/12', signal)

    expect(fetchMock).toHaveBeenCalledWith(
      '/plugins-api/api/operations?limit=200&cursor=2026-10-10T10%3A15%3A00Z%7Cop%2F12',
      { method: 'GET', signal },
    )
  })
})
