import { describe, expect, it } from 'vitest'

import {
  deriveActionAvailability,
  type ControlPlaneInput,
} from '../../control-plane/model'

function createOverviewInput(
  serverStatus: ControlPlaneInput['serverStatus'],
): ControlPlaneInput {
  return {
    serverStatus,
    rustVersions: {
      status: 'available',
      data: {
        branch: 'public',
        installedBuildId: 12345678,
        running: null,
        availableBuildId: 12345679,
        updateAvailable: true,
      },
    },
    oxideVersions: {
      status: 'available',
      data: {
        installedVersion: '2.0.7725',
        runningVersion: null,
        availableVersion: '2.0.7726',
        updateAvailable: true,
      },
    },
    operationDetail: { status: 'available', data: null },
  }
}

describe('deriveActionAvailability', () => {
  it('allows Start and eligible updates only when stopped', () => {
    expect(
      deriveActionAvailability(
        createOverviewInput({
          status: 'available',
          data: { isRunning: false, isReady: false },
        }),
      ),
    ).toEqual({
      start: true,
      stop: false,
      restart: false,
      rustUpdate: true,
      oxideUpdate: true,
    })
  })

  it('offers Rust installation but not Start or Oxide installation on a clean host', () => {
    const overview = createOverviewInput({
      status: 'available',
      data: { isRunning: false, isReady: false },
    })
    overview.rustVersions = {
      status: 'available',
      data: {
        branch: 'public',
        installedBuildId: null,
        running: null,
        availableBuildId: 12345679,
        updateAvailable: true,
      },
    }
    overview.oxideVersions = {
      status: 'available',
      data: {
        installedVersion: null,
        runningVersion: null,
        availableVersion: '2.0.7726',
        updateAvailable: true,
      },
    }

    expect(deriveActionAvailability(overview)).toEqual({
      start: false,
      stop: false,
      restart: false,
      rustUpdate: true,
      oxideUpdate: false,
    })
  })

  it('allows Oxide installation and Start once Rust is installed', () => {
    const overview = createOverviewInput({
      status: 'available',
      data: { isRunning: false, isReady: false },
    })
    overview.oxideVersions = {
      status: 'available',
      data: {
        installedVersion: null,
        runningVersion: null,
        availableVersion: '2.0.7726',
        updateAvailable: true,
      },
    }
    expect(deriveActionAvailability(overview)).toEqual({
      start: true,
      stop: false,
      restart: false,
      rustUpdate: true,
      oxideUpdate: true,
    })
  })

  it('disables Start when Rust installation status is unavailable', () => {
    const overview = createOverviewInput({
      status: 'available',
      data: { isRunning: false, isReady: false },
    })
    overview.rustVersions = { status: 'unavailable', message: 'Versions unavailable.' }
    expect(deriveActionAvailability(overview).start).toBe(false)
  })

  it('allows only Stop while starting', () => {
    expect(
      deriveActionAvailability(
        createOverviewInput({
          status: 'available',
          data: { isRunning: true, isReady: false },
        }),
      ),
    ).toEqual({
      start: false,
      stop: true,
      restart: false,
      rustUpdate: false,
      oxideUpdate: false,
    })
  })

  it('allows Stop and Restart when ready', () => {
    expect(
      deriveActionAvailability(
        createOverviewInput({
          status: 'available',
          data: { isRunning: true, isReady: true },
        }),
      ),
    ).toEqual({
      start: false,
      stop: true,
      restart: true,
      rustUpdate: false,
      oxideUpdate: false,
    })
  })

  it('disables every action for an inconsistent status', () => {
    expect(
      deriveActionAvailability(
        createOverviewInput({
          status: 'available',
          data: { isRunning: false, isReady: true },
        }),
      ),
    ).toEqual({
      start: false,
      stop: false,
      restart: false,
      rustUpdate: false,
      oxideUpdate: false,
    })
  })

  it('disables every action while an update is active', () => {
    const overview = createOverviewInput({
      status: 'available',
      data: { isRunning: false, isReady: false },
    })

    overview.operationDetail = {
      status: 'available',
      data: {
        id: 'f1ccbe0c-a923-4a38-8f3d-9c0f58b4c0d4',
        type: 'OxideUpdate',
        status: 'Pending',
        createdAt: '2026-09-29T08:30:00Z',
        startedAt: null,
        finishedAt: null,
        exitCode: null,
        error: null,
      },
    }

    expect(deriveActionAvailability(overview)).toEqual({
      start: false,
      stop: false,
      restart: false,
      rustUpdate: false,
      oxideUpdate: false,
    })
  })

  it('disables every action while an accepted operation detail is loading', () => {
    const overview = createOverviewInput({
      status: 'available',
      data: { isRunning: false, isReady: false },
    })
    overview.operationDetail = { status: 'loading' }

    expect(deriveActionAvailability(overview)).toEqual({
      start: false,
      stop: false,
      restart: false,
      rustUpdate: false,
      oxideUpdate: false,
    })
  })

  it('disables every action for an operation with an unknown status', () => {
    const overview = createOverviewInput({
      status: 'available',
      data: { isRunning: false, isReady: false },
    })

    overview.operationDetail = {
      status: 'available',
      data: {
        id: 'f1ccbe0c-a923-4a38-8f3d-9c0f58b4c0d4',
        type: 'RustUpdate',
        status: 'WaitingForApproval',
        createdAt: '2026-09-29T08:30:00Z',
        startedAt: null,
        finishedAt: null,
        exitCode: null,
        error: null,
      },
    }

    expect(deriveActionAvailability(overview)).toEqual({
      start: false,
      stop: false,
      restart: false,
      rustUpdate: false,
      oxideUpdate: false,
    })
  })

  it.each(['Succeeded', 'Failed'])(
    'allows current stopped-state actions after a terminal %s operation',
    (status) => {
      const overview = createOverviewInput({
        status: 'available',
        data: { isRunning: false, isReady: false },
      })
      overview.operationDetail = {
        status: 'available',
        data: {
          id: 'f1ccbe0c-a923-4a38-8f3d-9c0f58b4c0d4',
          type: 'RustUpdate',
          status,
          createdAt: '2026-09-29T08:30:00Z',
        },
      }

      expect(deriveActionAvailability(overview)).toEqual({
        start: true,
        stop: false,
        restart: false,
        rustUpdate: true,
        oxideUpdate: true,
      })
    },
  )

  it('uses the normal documented policy when operation detail is unavailable', () => {
    const overview = createOverviewInput({
      status: 'available',
      data: { isRunning: false, isReady: false },
    })
    overview.operationDetail = { status: 'unavailable' }

    expect(deriveActionAvailability(overview)).toEqual({
      start: true,
      stop: false,
      restart: false,
      rustUpdate: true,
      oxideUpdate: true,
    })
  })
})
