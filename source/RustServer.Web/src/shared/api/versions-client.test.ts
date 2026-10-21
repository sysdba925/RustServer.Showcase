import { afterEach, describe, expect, it, vi } from 'vitest'

import type { OxideVersionInfo, RustVersionInfo } from './versions-types'
import { getOxideVersions, getRustVersions } from './versions-client'

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('Versions API client', () => {
  it('requests the Rust and Oxide endpoints and returns their JSON', async () => {
    const expectedRustVersions: RustVersionInfo = {
      branch: 'public',
      installedBuildId: 12345678,
      running: null,
      availableBuildId: 12345679,
      updateAvailable: true,
    }
    const expectedOxideVersions: OxideVersionInfo = {
      installedVersion: '2.0.7725',
      runningVersion: null,
      availableVersion: '2.0.7726',
      updateAvailable: true,
    }
    const fetchMock = vi
      .fn()
      .mockResolvedValueOnce({
        ok: true,
        json: vi.fn().mockResolvedValue(expectedRustVersions),
      } as unknown as Response)
      .mockResolvedValueOnce({
        ok: true,
        json: vi.fn().mockResolvedValue(expectedOxideVersions),
      } as unknown as Response)
    vi.stubGlobal('fetch', fetchMock)

    const rustVersions: RustVersionInfo = await getRustVersions()
    const oxideVersions: OxideVersionInfo = await getOxideVersions()

    expect(fetchMock).toHaveBeenNthCalledWith(
      1,
      '/versions-api/api/versions/rust',
      { method: 'GET', signal: undefined },
    )
    expect(fetchMock).toHaveBeenNthCalledWith(
      2,
      '/versions-api/api/versions/oxide',
      { method: 'GET', signal: undefined },
    )
    expect(rustVersions).toEqual(expectedRustVersions)
    expect(oxideVersions).toEqual(expectedOxideVersions)
  })
})
