import { afterEach, describe, expect, it, vi } from 'vitest'

import {
  changeEnvironment,
  createRelease,
  deployDevelopmentToStaging,
  deployReleaseToProduction,
  deployReleaseToStaging,
  commitUpdatePackage,
  getEnvironment,
  getIncomingChangeDiff,
  getIncomingChanges,
  getProductionDeployment,
  getRelease,
  getReleases,
  getUpdatePackage,
  getStagingRuntimeChangeDiff,
  getStagingRuntimeChanges,
  importStagingRuntimeChanges,
  discardStagingRuntimeChanges,
  uploadUpdatePackage,
} from './plugins-client'
import type { ReleaseResponse, UpdatePackageResponse } from './plugins-types'

afterEach(() => vi.unstubAllGlobals())

const release: ReleaseResponse = {
  releaseId: '12',
  pluginsSha: 'plugins-sha',
  oxideConfigsSha: 'configs-sha',
  createdAt: '2026-10-01T10:00:00Z',
}

const updatePackage: UpdatePackageResponse = {
  id: 'package-1', originalFileName: 'Example.zip', status: 'inspected', createdAt: '2026-10-01T10:00:00Z', files: [], commits: [],
}

function successfulResponse(body: unknown): Response {
  return { ok: true, json: vi.fn().mockResolvedValue(body) } as unknown as Response
}

describe('Plugins API client', () => {
  it('uses the published Environment read and change paths', async () => {
    const environment = { environment: 'production' as const }
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(successfulResponse(environment))
      .mockResolvedValueOnce(successfulResponse(environment))
    vi.stubGlobal('fetch', fetchMock)

    await expect(getEnvironment()).resolves.toEqual(environment)
    await expect(changeEnvironment({ environment: 'production' })).resolves.toEqual(environment)

    expect(fetchMock).toHaveBeenNthCalledWith(1, '/plugins-api/api/environment', {
      method: 'GET',
      signal: undefined,
    })
    expect(fetchMock).toHaveBeenNthCalledWith(2, '/plugins-api/api/environment', {
      method: 'PUT',
      signal: undefined,
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ environment: 'production' }),
    })
  })

  it('uses the published release catalog paths', async () => {
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(successfulResponse([release]))
      .mockResolvedValueOnce(successfulResponse(release))
      .mockResolvedValueOnce(successfulResponse(release))
    vi.stubGlobal('fetch', fetchMock)

    await expect(getReleases()).resolves.toEqual([release])
    await expect(getRelease('release/id')).resolves.toEqual(release)
    await expect(createRelease()).resolves.toEqual(release)

    expect(fetchMock).toHaveBeenNthCalledWith(1, '/plugins-api/api/releases', { method: 'GET', signal: undefined })
    expect(fetchMock).toHaveBeenNthCalledWith(2, '/plugins-api/api/releases/release%2Fid', { method: 'GET', signal: undefined })
    expect(fetchMock).toHaveBeenNthCalledWith(3, '/plugins-api/api/releases', { method: 'POST', signal: undefined })
  })

  it('uses only the published explicit deployment paths', async () => {
    const deployment = { ...release, source: 'release', deployedFiles: 4, deployedAt: release.createdAt }
    const development = { source: 'development', pluginsSha: release.pluginsSha, oxideConfigsSha: release.oxideConfigsSha, deployedFiles: 4, deployedAt: release.createdAt }
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(successfulResponse(development))
      .mockResolvedValueOnce(successfulResponse(deployment))
      .mockResolvedValueOnce(successfulResponse(deployment))
      .mockResolvedValueOnce(successfulResponse(deployment))
    vi.stubGlobal('fetch', fetchMock)

    await deployDevelopmentToStaging()
    await deployReleaseToStaging('12')
    await deployReleaseToProduction('12')
    await getProductionDeployment()

    expect(fetchMock).toHaveBeenNthCalledWith(1, '/plugins-api/api/deployments/development', { method: 'POST', signal: undefined })
    expect(fetchMock).toHaveBeenNthCalledWith(2, '/plugins-api/api/staging/releases/12/deploy', { method: 'POST', signal: undefined })
    expect(fetchMock).toHaveBeenNthCalledWith(3, '/plugins-api/api/production/releases/12/deploy', { method: 'POST', signal: undefined })
    expect(fetchMock).toHaveBeenNthCalledWith(4, '/plugins-api/api/production/deployment', { method: 'GET', signal: undefined })
  })

  it('uses the published update-package endpoints and preserves multipart input', async () => {
    const comparison = { packageId: 'package-1', workspaceHeads: [{ repository: 'plugins' as const, head: 'plugins-head' }], changes: [] }
    const diff = { packageId: 'package-1', path: 'oxide/plugins/Example.cs', repository: 'plugins' as const, changeType: 'added' as const, workspaceHead: 'plugins-head', workspaceText: null, incomingText: 'class Example {}' }
    const commit = { packageId: 'package-1', commits: [] }
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(successfulResponse(updatePackage))
      .mockResolvedValueOnce(successfulResponse(updatePackage))
      .mockResolvedValueOnce(successfulResponse(comparison))
      .mockResolvedValueOnce(successfulResponse(diff))
      .mockResolvedValueOnce(successfulResponse(commit))
    vi.stubGlobal('fetch', fetchMock)

    await uploadUpdatePackage(new File(['source'], 'Example.cs', { type: 'text/plain' }))
    await getUpdatePackage('package/1')
    await getIncomingChanges('package/1')
    await getIncomingChangeDiff('package/1', 'oxide/plugins/Example.cs', 'plugins-head')
    await commitUpdatePackage('package/1', { acceptedPaths: ['oxide/plugins/Example.cs'], expectedWorkspaceHeads: { plugins: 'plugins-head' }, message: 'Apply example' })

    expect(fetchMock).toHaveBeenNthCalledWith(1, '/plugins-api/api/update-packages', expect.objectContaining({ method: 'POST', body: expect.any(FormData) }))
    expect(fetchMock.mock.calls[0][1].headers).toBeUndefined()
    expect(fetchMock).toHaveBeenNthCalledWith(2, '/plugins-api/api/update-packages/package%2F1', { method: 'GET', signal: undefined })
    expect(fetchMock).toHaveBeenNthCalledWith(3, '/plugins-api/api/update-packages/package%2F1/changes', { method: 'GET', signal: undefined })
    expect(fetchMock).toHaveBeenNthCalledWith(4, '/plugins-api/api/update-packages/package%2F1/diff?path=oxide%2Fplugins%2FExample.cs&workspaceHead=plugins-head', { method: 'GET', signal: undefined })
    expect(fetchMock).toHaveBeenNthCalledWith(5, '/plugins-api/api/update-packages/package%2F1/commit', { method: 'POST', signal: undefined, headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ acceptedPaths: ['oxide/plugins/Example.cs'], expectedWorkspaceHeads: { plugins: 'plugins-head' }, message: 'Apply example' }) })
  })

  it('uses only the published Staging runtime-change endpoints and token selections', async () => {
    const comparison = { source: 'development', pluginsSha: 'plugins-sha', oxideConfigsSha: 'configs-sha', changes: [] }
    const diff = { path: 'oxide/plugins/Example.cs', changeType: 'modified' as const, baselineText: 'old', runtimeText: 'new' }
    const importResult = { commits: [] }
    const discardResult = { discardedPaths: ['oxide/plugins/Example.cs'] }
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(successfulResponse(comparison))
      .mockResolvedValueOnce(successfulResponse(diff))
      .mockResolvedValueOnce(successfulResponse(importResult))
      .mockResolvedValueOnce(successfulResponse(discardResult))
    vi.stubGlobal('fetch', fetchMock)

    await getStagingRuntimeChanges()
    await getStagingRuntimeChangeDiff('oxide/plugins/Example.cs')
    await importStagingRuntimeChanges({ message: 'Import runtime state', changes: [{ path: 'oxide/plugins/Example.cs', expectedChangeToken: 'opaque-token' }] })
    await discardStagingRuntimeChanges({ changes: [{ path: 'oxide/plugins/Example.cs', expectedChangeToken: 'opaque-token' }] })

    expect(fetchMock).toHaveBeenNthCalledWith(1, '/plugins-api/api/staging/runtime-changes', { method: 'GET', signal: undefined })
    expect(fetchMock).toHaveBeenNthCalledWith(2, '/plugins-api/api/staging/runtime-changes/diff?path=oxide%2Fplugins%2FExample.cs', { method: 'GET', signal: undefined })
    expect(fetchMock).toHaveBeenNthCalledWith(3, '/plugins-api/api/staging/runtime-changes/import', { method: 'POST', signal: undefined, headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ message: 'Import runtime state', changes: [{ path: 'oxide/plugins/Example.cs', expectedChangeToken: 'opaque-token' }] }) })
    expect(fetchMock).toHaveBeenNthCalledWith(4, '/plugins-api/api/staging/runtime-changes/discard', { method: 'POST', signal: undefined, headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ changes: [{ path: 'oxide/plugins/Example.cs', expectedChangeToken: 'opaque-token' }] }) })
  })
})
