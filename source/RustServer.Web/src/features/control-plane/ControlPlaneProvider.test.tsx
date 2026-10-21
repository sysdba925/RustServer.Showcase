import { act, fireEvent, render, screen } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import {
  HttpError,
  type OxideVersionInfo,
  type RustVersionInfo,
  type ServerConfigurationResponse,
  type ServerStatus,
  type StartupSchemaResponse,
  type StartupSnapshotResponse,
} from '../../shared/api'
import { useControlPlane } from './ControlPlaneContext'
import { ControlPlaneProvider } from './ControlPlaneProvider'
import { ServerRuntimePanel } from '../server/components/ServerRuntimePanel'
import { StartupTracking } from '../server/components/StartupTracking'
import { ActivityPanel } from '../overview/components/ActivityPanel'
import { SoftwarePanel } from '../overview/components/SoftwarePanel'

const api = vi.hoisted(() => ({
  getServerStatus: vi.fn(),
  getServerConfiguration: vi.fn(),
  getStartupSchema: vi.fn(),
  getStartupSnapshot: vi.fn(),
  getRustVersions: vi.fn(),
  getOxideVersions: vi.fn(),
  getOperation: vi.fn(),
  startServer: vi.fn(),
  stopServer: vi.fn(),
  restartServer: vi.fn(),
  requestRustUpdate: vi.fn(),
  requestOxideUpdate: vi.fn(),
  updateServerConfiguration: vi.fn(),
  requestWipe: vi.fn(),
}))

vi.mock('../../shared/api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../shared/api')>()

  return { ...actual, ...api }
})

interface Deferred<T> {
  promise: Promise<T>
  resolve: (value: T) => void
  reject: (reason?: unknown) => void
}

function createDeferred<T>(): Deferred<T> {
  let resolvePromise: (value: T) => void
  let rejectPromise: (reason?: unknown) => void
  const promise = new Promise<T>((resolve, reject) => {
    resolvePromise = resolve
    rejectPromise = reject
  })

  return {
    promise,
    resolve: resolvePromise!,
    reject: rejectPromise!,
  }
}

function ControlPlaneTestSurfaceContents() {
  const controlPlane = useControlPlane()

  return (
    <>
      <ServerRuntimePanel
        actionAvailability={controlPlane.actionAvailability}
        lifecycleError={controlPlane.lifecycleError}
        mutationsBlocked={controlPlane.mutationsBlocked}
        onRestart={controlPlane.restart}
        onStart={controlPlane.start}
        onStop={controlPlane.stop}
        operationDetail={controlPlane.operationDetail}
        pendingLifecycleAction={controlPlane.pendingLifecycleAction}
        serverStatus={controlPlane.serverStatus}
        rustVersions={controlPlane.rustVersions}
      />
      <StartupTracking
        startupSchema={controlPlane.startupSchema}
        startupSnapshot={controlPlane.startupSnapshot}
      />
      <SoftwarePanel
        actionAvailability={controlPlane.actionAvailability}
        actionsBlocked={controlPlane.mutationsBlocked}
        onOxideUpdate={controlPlane.requestOxideUpdate}
        onRustUpdate={controlPlane.requestRustUpdate}
        oxideUpdateError={controlPlane.oxideUpdateError}
        oxideUpdateUnavailableReason={controlPlane.updateUnavailableReason}
        oxideVersions={controlPlane.oxideVersions}
        pendingUpdateTarget={controlPlane.pendingUpdateTarget}
        rustUpdateError={controlPlane.rustUpdateError}
        rustUpdateUnavailableReason={controlPlane.updateUnavailableReason}
        rustVersions={controlPlane.rustVersions}
      />
      <ActivityPanel
        acceptedOperation={controlPlane.acceptedOperation}
        operationDetail={controlPlane.operationDetail}
      />
    </>
  )
}

function ControlPlaneTestSurface() {
  return (
    <ControlPlaneProvider>
      <ControlPlaneTestSurfaceContents />
    </ControlPlaneProvider>
  )
}

function ConfigurationAndWipeProbe() {
  const controlPlane = useControlPlane()
  const desired =
    controlPlane.configuration.status === 'available'
      ? controlPlane.configuration.data.desired
      : null

  return (
    <>
      <p>{desired ? `Desired seed: ${desired.seed}` : 'Configuration loading'}</p>
      <button
        disabled={controlPlane.mutationsBlocked || desired === null}
        onClick={() => {
          if (desired) {
            controlPlane.saveConfiguration({ ...desired, seed: 67890 })
          }
        }}
        type="button"
      >
        Save desired seed
      </button>
      <button
        disabled={controlPlane.wipeUnavailableReason !== null}
        onClick={() =>
          controlPlane.requestWipe({
            world: 'delete',
            blueprints: 'keep',
            pluginData: [],
          })
        }
        type="button"
      >
        Request wipe
      </button>
      {controlPlane.wipeUnavailableReason && (
        <p>{controlPlane.wipeUnavailableReason}</p>
      )}
      {controlPlane.operationDetail.status === 'available' &&
        controlPlane.operationDetail.data?.wipeResult && (
          <p>
            Wipe affected items:{' '}
            {controlPlane.operationDetail.data.wipeResult.targets[0]?.affectedItems}
          </p>
        )}
    </>
  )
}

function ConfigurationAndWipeTestSurface() {
  return (
    <ControlPlaneProvider>
      <ConfigurationAndWipeProbe />
    </ControlPlaneProvider>
  )
}

const readyServerStatus: ServerStatus = {
  isRunning: true,
  isReady: true,
}

const stoppedServerStatus: ServerStatus = {
  isRunning: false,
  isReady: false,
}

const serverConfiguration: ServerConfigurationResponse = {
  desired: {
    hostname: 'Test server',
    description: 'Test description',
    maxPlayers: 50,
    tickRate: 30,
    saveInterval: 300,
    seed: 12345,
    worldSize: 4200,
  },
  effective: null,
}

const startupSchema: StartupSchemaResponse = {
  version: 1,
  milestones: [
    { id: 'engine', label: 'Unity Engine' },
    { id: 'mono', label: 'Mono Runtime' },
  ],
}

const stoppedStartupSnapshot: StartupSnapshotResponse = {
  sessionId: null,
  started: false,
  milestones: [],
}

const rustVersions: RustVersionInfo = {
  branch: 'public',
  installedBuildId: 12345678,
  running: null,
  availableBuildId: 12345679,
  updateAvailable: true,
}

const oxideVersions: OxideVersionInfo = {
  installedVersion: '2.0.7725',
  runningVersion: null,
  availableVersion: '2.0.7726',
  updateAvailable: true,
}

const runningRustVersions: RustVersionInfo = {
  ...rustVersions,
  running: {
    protocol: '2517',
    buildDate: '2026-09-29T10:00:00Z',
    unityVersion: '2022.3.55f1',
    changeset: 987654,
    branch: 'public',
  },
}

const runningOxideVersions: OxideVersionInfo = {
  ...oxideVersions,
  runningVersion: '2.0.7725',
}

function createOperation(
  status: string,
  type: string = 'RustUpdate',
  optionalFields: Partial<{
    startedAt: string | null
    finishedAt: string | null
    exitCode: number | null
    error: string | null
  }> = {},
) {
  return {
    id: 'f1ccbe0c-a923-4a38-8f3d-9c0f58b4c0d4',
    type,
    status,
    createdAt: '2026-09-29T08:30:00Z',
    ...optionalFields,
  }
}

function mockInitialResources(serverStatus: ServerStatus): void {
  api.getServerStatus.mockResolvedValue(serverStatus)
  api.getServerConfiguration.mockResolvedValue(serverConfiguration)
  api.getStartupSchema.mockResolvedValue(startupSchema)
  api.getStartupSnapshot.mockResolvedValue(stoppedStartupSnapshot)
  api.getRustVersions.mockResolvedValue(rustVersions)
  api.getOxideVersions.mockResolvedValue(oxideVersions)
}

async function flushReactUpdates(): Promise<void> {
  await act(async () => {})
}

async function advancePollingTime(milliseconds: number): Promise<void> {
  await act(async () => {
    await vi.advanceTimersByTimeAsync(milliseconds)
  })
}

beforeEach(() => {
  api.getServerStatus.mockReset()
  api.getServerConfiguration.mockReset()
  api.getStartupSchema.mockReset()
  api.getStartupSnapshot.mockReset()
  api.getRustVersions.mockReset()
  api.getOxideVersions.mockReset()
  api.getOperation.mockReset()
  api.startServer.mockReset()
  api.stopServer.mockReset()
  api.restartServer.mockReset()
  api.requestRustUpdate.mockReset()
  api.requestOxideUpdate.mockReset()
  api.updateServerConfiguration.mockReset()
  api.requestWipe.mockReset()
  api.getServerConfiguration.mockResolvedValue(serverConfiguration)
  api.getStartupSchema.mockResolvedValue(startupSchema)
  api.getStartupSnapshot.mockResolvedValue(stoppedStartupSnapshot)
})

afterEach(() => {
  vi.useRealTimers()
})

describe('ControlPlaneProvider', () => {
  it('keeps configuration authoritative after a full desired-configuration save', async () => {
    const updatedConfiguration = {
      ...serverConfiguration,
      desired: { ...serverConfiguration.desired, seed: 67890 },
    }
    mockInitialResources(stoppedServerStatus)
    api.updateServerConfiguration.mockResolvedValue(updatedConfiguration)

    render(<ConfigurationAndWipeTestSurface />)

    fireEvent.click(await screen.findByRole('button', { name: 'Save desired seed' }))

    expect(api.updateServerConfiguration).toHaveBeenCalledWith(
      updatedConfiguration.desired,
      expect.any(AbortSignal),
    )
    expect(await screen.findByText('Desired seed: 67890')).toBeVisible()
  })

  it('routes a wipe through the shared operation poller and reconciles after completion', async () => {
    mockInitialResources(stoppedServerStatus)
    api.requestWipe.mockResolvedValue({
      id: 'f1ccbe0c-a923-4a38-8f3d-9c0f58b4c0d4',
      status: 'Pending',
    })
    api.getOperation.mockResolvedValue({
      ...createOperation('Succeeded', 'Wipe'),
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
          {
            target: 'blueprints',
            action: 'keep',
            outcome: 'kept',
            affectedItems: 0,
            error: null,
          },
        ],
      },
    })

    render(<ConfigurationAndWipeTestSurface />)

    fireEvent.click(await screen.findByRole('button', { name: 'Request wipe' }))

    expect(api.requestWipe).toHaveBeenCalledWith(
      { world: 'delete', blueprints: 'keep', pluginData: [] },
      expect.any(AbortSignal),
    )
    expect(await screen.findByText('Wipe affected items: 2')).toBeVisible()
    expect(api.getOperation).toHaveBeenCalledWith(
      'f1ccbe0c-a923-4a38-8f3d-9c0f58b4c0d4',
      expect.any(AbortSignal),
    )
    expect(api.stopServer).not.toHaveBeenCalled()
    expect(api.startServer).not.toHaveBeenCalled()
  })

  it('keeps wipe unavailable while the server is running', async () => {
    mockInitialResources(readyServerStatus)

    render(<ConfigurationAndWipeTestSurface />)

    expect(await screen.findByText('Server must be stopped to wipe.')).toBeVisible()
    expect(screen.getByRole('button', { name: 'Request wipe' })).toBeDisabled()
    expect(api.requestWipe).not.toHaveBeenCalled()
  })

  it('shows local loading states before the independent requests resolve', () => {
    const serverStatus = createDeferred<ServerStatus>()
    const rust = createDeferred<RustVersionInfo>()
    const oxide = createDeferred<OxideVersionInfo>()
    api.getServerStatus.mockReturnValue(serverStatus.promise)
    api.getRustVersions.mockReturnValue(rust.promise)
    api.getOxideVersions.mockReturnValue(oxide.promise)

    render(<ControlPlaneTestSurface />)

    expect(screen.getByText('Loading server status.')).toBeVisible()
    expect(screen.getByText('Loading Rust version information.')).toBeVisible()
    expect(screen.getByText('Loading Oxide version information.')).toBeVisible()
  })

  it('renders the values returned by all three API resources', async () => {
    mockInitialResources(readyServerStatus)

    render(<ControlPlaneTestSurface />)

    expect(
      await screen.findByText('Rust server is running and ready.'),
    ).toBeVisible()
    expect(await screen.findByText('12345678')).toBeVisible()
    expect(await screen.findByText('2.0.7725')).toBeVisible()
    expect(screen.getByText('No known operation.')).toBeVisible()
  })

  it('keeps successful resources visible when Rust versions fail', async () => {
    api.getServerStatus.mockResolvedValue(readyServerStatus)
    api.getRustVersions.mockRejectedValue(new Error('Versions service unavailable.'))
    api.getOxideVersions.mockResolvedValue(oxideVersions)

    render(<ControlPlaneTestSurface />)

    expect(
      await screen.findByText('Rust version information unavailable.'),
    ).toBeVisible()
    expect(screen.getByText('Versions service unavailable.')).toBeVisible()
    expect(screen.getByText('Rust server is running and ready.')).toBeVisible()
    expect(screen.getByText('2.0.7725')).toBeVisible()
  })

  it('uses a dedicated AbortSignal for software refreshes and aborts all requests on cleanup', () => {
    const pendingRequest = new Promise<never>(() => {})
    api.getServerStatus.mockReturnValue(pendingRequest)
    api.getStartupSchema.mockReturnValue(pendingRequest)
    api.getStartupSnapshot.mockReturnValue(pendingRequest)
    api.getRustVersions.mockReturnValue(pendingRequest)
    api.getOxideVersions.mockReturnValue(pendingRequest)

    const { unmount } = render(<ControlPlaneTestSurface />)
    const serverSignal = api.getServerStatus.mock.calls[0][0] as AbortSignal
    const startupSchemaSignal = api.getStartupSchema.mock.calls[0][0] as AbortSignal
    const startupSnapshotSignal = api.getStartupSnapshot.mock.calls[0][0] as AbortSignal
    const softwareSignal = api.getRustVersions.mock.calls[0][0] as AbortSignal

    expect(serverSignal).toBeInstanceOf(AbortSignal)
    expect(startupSchemaSignal).toBeInstanceOf(AbortSignal)
    expect(startupSnapshotSignal).toBeInstanceOf(AbortSignal)
    expect(softwareSignal).toBeInstanceOf(AbortSignal)
    expect(softwareSignal).not.toBe(serverSignal)
    expect(startupSchemaSignal).not.toBe(serverSignal)
    expect(startupSnapshotSignal).not.toBe(serverSignal)
    expect(api.getOxideVersions).toHaveBeenCalledWith(softwareSignal)

    unmount()

    expect(serverSignal.aborted).toBe(true)
    expect(startupSchemaSignal.aborted).toBe(true)
    expect(startupSnapshotSignal.aborted).toBe(true)
    expect(softwareSignal.aborted).toBe(true)
  })

  it('loads the startup schema once and polls an incomplete snapshot independently of readiness', async () => {
    vi.useFakeTimers()
    mockInitialResources(readyServerStatus)
    api.getStartupSnapshot
      .mockResolvedValueOnce({
        sessionId: 'stp_run-a',
        started: false,
        milestones: ['reached', 'pending'],
      })
      .mockResolvedValueOnce({
        sessionId: 'stp_run-a',
        started: true,
        milestones: ['reached', 'pending'],
      })

    render(<ControlPlaneTestSurface />)
    await flushReactUpdates()

    expect(api.getStartupSchema).toHaveBeenCalledOnce()
    expect(api.getStartupSnapshot).toHaveBeenCalledOnce()
    expect(screen.getByText('Startup milestones are being observed.')).toBeVisible()
    expect(api.getServerStatus).toHaveBeenCalledOnce()

    await advancePollingTime(2_000)

    expect(api.getStartupSnapshot).toHaveBeenCalledTimes(2)
    expect(screen.getByText('Startup milestones complete.')).toBeVisible()
    expect(api.getServerStatus).toHaveBeenCalledOnce()

    await advancePollingTime(8_000)

    expect(api.getStartupSchema).toHaveBeenCalledOnce()
    expect(api.getStartupSnapshot).toHaveBeenCalledTimes(2)
  })

  it('replaces rather than merges progress when the startup session changes', async () => {
    vi.useFakeTimers()
    mockInitialResources(readyServerStatus)
    api.getStartupSnapshot
      .mockResolvedValueOnce({
        sessionId: 'stp_run-a',
        started: false,
        milestones: ['reached', 'reached'],
      })
      .mockResolvedValueOnce({
        sessionId: 'stp_run-b',
        started: false,
        milestones: ['reached', 'pending'],
      })

    render(<ControlPlaneTestSurface />)
    await flushReactUpdates()

    const milestones = screen.getByRole('list', { name: 'Startup milestones' })
    expect(milestones).toHaveTextContent('Mono Runtime')
    expect(screen.getAllByText('Reached')).toHaveLength(2)

    await advancePollingTime(2_000)

    expect(milestones).toHaveTextContent('Pending')
    expect(screen.getAllByText('Reached')).toHaveLength(1)
  })

  it('polls for a startup session while Rust is Starting even if the first snapshot is empty', async () => {
    vi.useFakeTimers()
    mockInitialResources({ isRunning: true, isReady: false })
    api.getStartupSnapshot
      .mockResolvedValueOnce(stoppedStartupSnapshot)
      .mockResolvedValueOnce({
        sessionId: 'stp_run-a',
        started: false,
        milestones: ['reached', 'pending'],
      })
      .mockResolvedValueOnce({
        sessionId: 'stp_run-a',
        started: true,
        milestones: ['reached', 'reached'],
      })

    render(<ControlPlaneTestSurface />)
    await flushReactUpdates()
    expect(screen.getByText('No active startup session.')).toBeVisible()

    await advancePollingTime(2_000)
    expect(api.getStartupSnapshot).toHaveBeenCalledTimes(2)
    expect(screen.getByText('Startup milestones are being observed.')).toBeVisible()

    await advancePollingTime(2_000)
    expect(api.getStartupSnapshot).toHaveBeenCalledTimes(3)
    expect(screen.getByText('Startup milestones complete.')).toBeVisible()
  })

  it('observes startup while Start is pending and resumes after its response refreshes an empty snapshot', async () => {
    vi.useFakeTimers()
    mockInitialResources(stoppedServerStatus)
    const start = createDeferred<ServerStatus>()
    api.startServer.mockReturnValue(start.promise)
    api.getStartupSnapshot
      .mockResolvedValueOnce(stoppedStartupSnapshot)
      .mockResolvedValueOnce(stoppedStartupSnapshot)
      .mockResolvedValueOnce(stoppedStartupSnapshot)
      .mockResolvedValueOnce({
        sessionId: 'stp_run-b',
        started: false,
        milestones: ['reached', 'pending'],
      })

    render(<ControlPlaneTestSurface />)
    await flushReactUpdates()
    fireEvent.click(screen.getByRole('button', { name: 'Start' }))
    await flushReactUpdates()

    await advancePollingTime(2_000)
    expect(api.startServer).toHaveBeenCalledOnce()
    expect(api.getStartupSnapshot).toHaveBeenCalledTimes(2)

    await act(async () => {
      start.resolve({ isRunning: true, isReady: false })
    })
    await flushReactUpdates()
    expect(api.getStartupSnapshot).toHaveBeenCalledTimes(3)

    await advancePollingTime(2_000)
    expect(api.getStartupSnapshot).toHaveBeenCalledTimes(4)
    expect(screen.getByText('Startup milestones are being observed.')).toBeVisible()
  })

  it('does not poll the documented empty startup snapshot while stopped', async () => {
    vi.useFakeTimers()
    mockInitialResources(stoppedServerStatus)

    render(<ControlPlaneTestSurface />)
    await flushReactUpdates()

    expect(screen.getByText('No active startup session.')).toBeVisible()

    await advancePollingTime(10_000)

    expect(api.getStartupSnapshot).toHaveBeenCalledOnce()
  })

  it('starts the server and applies the synchronous response without another status request', async () => {
    const start = createDeferred<ServerStatus>()
    mockInitialResources(stoppedServerStatus)
    api.startServer.mockReturnValue(start.promise)

    render(<ControlPlaneTestSurface />)

    const startButton = await screen.findByRole('button', { name: 'Start' })
    fireEvent.click(startButton)

    expect(api.startServer).toHaveBeenCalledOnce()
    expect(screen.getByRole('status')).toHaveTextContent('Starting...')
    expect(screen.getByRole('button', { name: 'Starting...' })).toBeDisabled()
    expect(screen.getByRole('button', { name: 'Stop' })).toBeDisabled()
    expect(screen.getByRole('button', { name: 'Restart' })).toBeDisabled()

    start.resolve({ isRunning: true, isReady: false })

    expect(
      await screen.findByText('Rust process is running. Waiting for server readiness.'),
    ).toBeVisible()
    expect(screen.getByRole('article', { name: 'Server state' })).toHaveTextContent(
      /Running\s*Yes/,
    )
    expect(screen.getByRole('article', { name: 'Server state' })).toHaveTextContent(
      /Ready\s*No/,
    )
    expect(api.getServerStatus).toHaveBeenCalledOnce()
    expect(api.getRustVersions).toHaveBeenCalledTimes(2)
    expect(api.getOxideVersions).toHaveBeenCalledTimes(2)
  })

  it('stops the server, immediately applies its response, and reconciles both software resources', async () => {
    api.getServerStatus.mockResolvedValue(readyServerStatus)
    api.getRustVersions
      .mockResolvedValueOnce(runningRustVersions)
      .mockResolvedValueOnce(rustVersions)
    api.getOxideVersions
      .mockResolvedValueOnce(runningOxideVersions)
      .mockResolvedValueOnce(oxideVersions)
    api.stopServer.mockResolvedValue(stoppedServerStatus)

    render(<ControlPlaneTestSurface />)

    fireEvent.click(await screen.findByRole('button', { name: 'Stop' }))

    expect(api.stopServer).toHaveBeenCalledOnce()
    expect(await screen.findByText('Rust server is stopped.')).toBeVisible()
    expect(screen.getByRole('article', { name: 'Server state' })).toHaveTextContent(
      /Running\s*No/,
    )
    expect(screen.getByRole('article', { name: 'Server state' })).toHaveTextContent(
      /Ready\s*No/,
    )
    expect(api.getRustVersions).toHaveBeenCalledTimes(2)
    expect(api.getOxideVersions).toHaveBeenCalledTimes(2)
    expect(screen.getByRole('article', { name: 'Rust version information' })).toHaveTextContent(
      /Running\s*Not running/,
    )
    expect(screen.getByRole('article', { name: 'Oxide version information' })).toHaveTextContent(
      /Running\s*Not running/,
    )
  })

  it('restarts the server and applies the synchronous response', async () => {
    mockInitialResources(readyServerStatus)
    api.restartServer.mockResolvedValue({ isRunning: true, isReady: false })

    render(<ControlPlaneTestSurface />)

    fireEvent.click(await screen.findByRole('button', { name: 'Restart' }))

    expect(api.restartServer).toHaveBeenCalledOnce()
    expect(
      await screen.findByText('Rust process is running. Waiting for server readiness.'),
    ).toBeVisible()
    expect(api.getRustVersions).toHaveBeenCalledTimes(2)
    expect(api.getOxideVersions).toHaveBeenCalledTimes(2)
  })

  it('keeps the known server status visible when a lifecycle conflict returns ProblemDetails', async () => {
    mockInitialResources(readyServerStatus)
    api.restartServer.mockRejectedValue(
      new HttpError({
        method: 'POST',
        url: '/api/server/restart',
        status: 409,
        statusText: 'Conflict',
        problemDetails: {
          title: 'Restart unavailable',
          detail: 'An update operation is already active.',
          status: 409,
        },
      }),
    )

    render(<ControlPlaneTestSurface />)

    fireEvent.click(await screen.findByRole('button', { name: 'Restart' }))

    expect(
      await screen.findByText('An update operation is already active.'),
    ).toBeVisible()
    const panel = screen.getByRole('article', { name: 'Server state' })
    expect(panel).toHaveTextContent('Ready')
    expect(panel).toHaveTextContent(/Running\s*Yes/)
    expect(panel).toHaveTextContent(/Ready\s*Yes/)
    expect(screen.queryByText('Server status unavailable.')).not.toBeInTheDocument()
    expect(api.getRustVersions).toHaveBeenCalledOnce()
    expect(api.getOxideVersions).toHaveBeenCalledOnce()
  })

  it('uses a ProblemDetails title when a lifecycle failure has no detail', async () => {
    mockInitialResources(readyServerStatus)
    api.restartServer.mockRejectedValue(
      new HttpError({
        method: 'POST',
        url: '/api/server/restart',
        status: 409,
        statusText: 'Conflict',
        problemDetails: {
          title: 'Restart unavailable',
          detail: '   ',
          status: 409,
        },
      }),
    )

    render(<ControlPlaneTestSurface />)

    fireEvent.click(await screen.findByRole('button', { name: 'Restart' }))

    expect(await screen.findByText('Restart unavailable')).toBeVisible()
  })

  it('does not initiate another lifecycle mutation while one is pending', async () => {
    const restart = createDeferred<ServerStatus>()
    mockInitialResources(readyServerStatus)
    api.restartServer.mockReturnValue(restart.promise)

    render(<ControlPlaneTestSurface />)

    fireEvent.click(await screen.findByRole('button', { name: 'Restart' }))

    const stopButton = screen.getByRole('button', { name: 'Stop' })
    expect(stopButton).toBeDisabled()
    fireEvent.click(stopButton)

    expect(api.restartServer).toHaveBeenCalledOnce()
    expect(api.stopServer).not.toHaveBeenCalled()
  })

  it('reconciles a Start response from Starting to Ready without polling software while still Starting', async () => {
    vi.useFakeTimers()
    api.getServerStatus
      .mockResolvedValueOnce(stoppedServerStatus)
      .mockResolvedValueOnce({ isRunning: true, isReady: false })
      .mockResolvedValueOnce(readyServerStatus)
    api.getRustVersions
      .mockResolvedValueOnce(rustVersions)
      .mockResolvedValueOnce(rustVersions)
      .mockResolvedValueOnce(runningRustVersions)
    api.getOxideVersions
      .mockResolvedValueOnce(oxideVersions)
      .mockResolvedValueOnce(oxideVersions)
      .mockResolvedValueOnce(runningOxideVersions)
    api.startServer.mockResolvedValue({ isRunning: true, isReady: false })

    render(<ControlPlaneTestSurface />)
    await flushReactUpdates()

    fireEvent.click(screen.getByRole('button', { name: 'Start' }))
    await flushReactUpdates()

    expect(screen.getByText('Rust process is running. Waiting for server readiness.')).toBeVisible()
    expect(api.getServerStatus).toHaveBeenCalledOnce()
    expect(api.getRustVersions).toHaveBeenCalledTimes(2)
    expect(api.getOxideVersions).toHaveBeenCalledTimes(2)

    await advancePollingTime(2_000)

    expect(api.getServerStatus).toHaveBeenCalledTimes(2)
    expect(screen.getByText('Rust process is running. Waiting for server readiness.')).toBeVisible()
    expect(api.getRustVersions).toHaveBeenCalledTimes(2)
    expect(api.getOxideVersions).toHaveBeenCalledTimes(2)

    await advancePollingTime(1_999)

    expect(api.getServerStatus).toHaveBeenCalledTimes(2)

    await advancePollingTime(1)

    expect(api.getServerStatus).toHaveBeenCalledTimes(3)
    expect(screen.getByText('Rust server is running and ready.')).toBeVisible()
    expect(api.getRustVersions).toHaveBeenCalledTimes(3)
    expect(api.getOxideVersions).toHaveBeenCalledTimes(3)
    expect(screen.getByRole('article', { name: 'Rust version information' })).toHaveTextContent(
      /Protocol\s*2517/,
    )
    expect(screen.getByRole('article', { name: 'Oxide version information' })).toHaveTextContent(
      /Running\s*2\.0\.7725/,
    )

    await advancePollingTime(8_000)

    expect(api.getServerStatus).toHaveBeenCalledTimes(3)
  })

  it('reconciles an initial Starting status without a lifecycle mutation', async () => {
    vi.useFakeTimers()
    api.getServerStatus
      .mockResolvedValueOnce({ isRunning: true, isReady: false })
      .mockResolvedValueOnce(readyServerStatus)
    api.getRustVersions.mockResolvedValue(rustVersions)
    api.getOxideVersions.mockResolvedValue(oxideVersions)

    render(<ControlPlaneTestSurface />)
    await flushReactUpdates()

    expect(screen.getByText('Rust process is running. Waiting for server readiness.')).toBeVisible()
    expect(api.getServerStatus).toHaveBeenCalledOnce()

    await advancePollingTime(2_000)

    expect(api.getServerStatus).toHaveBeenCalledTimes(2)
    expect(screen.getByText('Rust server is running and ready.')).toBeVisible()
    expect(api.getRustVersions).toHaveBeenCalledTimes(2)
    expect(api.getOxideVersions).toHaveBeenCalledTimes(2)
  })

  it('keeps the Server available and reconciles Rust and Oxide independently after lifecycle success', async () => {
    api.getServerStatus.mockResolvedValue(readyServerStatus)
    api.getRustVersions
      .mockResolvedValueOnce(runningRustVersions)
      .mockRejectedValueOnce(new Error('Rust versions service unavailable.'))
    api.getOxideVersions
      .mockResolvedValueOnce(runningOxideVersions)
      .mockResolvedValueOnce({
        ...oxideVersions,
        installedVersion: '2.0.7726',
      })
    api.stopServer.mockResolvedValue(stoppedServerStatus)

    render(<ControlPlaneTestSurface />)

    fireEvent.click(await screen.findByRole('button', { name: 'Stop' }))

    expect(
      await screen.findByText('Rust version information unavailable.'),
    ).toBeVisible()
    expect(screen.getByText('Rust versions service unavailable.')).toBeVisible()
    expect(screen.getByText('Rust server is stopped.')).toBeVisible()
    expect(screen.getByRole('article', { name: 'Oxide version information' })).toHaveTextContent(
      /Installed\s*2\.0\.7726/,
    )
  })

  it('uses the latest software refresh when Starting reaches Ready', async () => {
    vi.useFakeTimers()
    const staleRust = createDeferred<RustVersionInfo>()
    const staleOxide = createDeferred<OxideVersionInfo>()
    const refreshedRust: RustVersionInfo = {
      ...runningRustVersions,
      installedBuildId: 33333333,
      running: {
        ...runningRustVersions.running!,
        protocol: 'latest-rust',
      },
    }
    const refreshedOxide: OxideVersionInfo = {
      ...runningOxideVersions,
      installedVersion: 'latest-oxide',
      runningVersion: 'latest-oxide',
    }

    api.getServerStatus
      .mockResolvedValueOnce(stoppedServerStatus)
      .mockResolvedValueOnce(readyServerStatus)
    api.getRustVersions
      .mockResolvedValueOnce(rustVersions)
      .mockReturnValueOnce(staleRust.promise)
      .mockResolvedValueOnce(refreshedRust)
    api.getOxideVersions
      .mockResolvedValueOnce(oxideVersions)
      .mockReturnValueOnce(staleOxide.promise)
      .mockResolvedValueOnce(refreshedOxide)
    api.startServer.mockResolvedValue({ isRunning: true, isReady: false })

    render(<ControlPlaneTestSurface />)
    await flushReactUpdates()

    fireEvent.click(screen.getByRole('button', { name: 'Start' }))
    await flushReactUpdates()

    const staleRustSignal = api.getRustVersions.mock.calls[1][0] as AbortSignal
    const staleOxideSignal = api.getOxideVersions.mock.calls[1][0] as AbortSignal

    expect(staleRustSignal.aborted).toBe(false)
    expect(staleOxideSignal.aborted).toBe(false)
    expect(screen.getByRole('article', { name: 'Rust version information' })).toHaveTextContent(
      /Installed\s*12345678/,
    )
    expect(screen.getByRole('article', { name: 'Oxide version information' })).toHaveTextContent(
      /Installed\s*2\.0\.7725/,
    )
    expect(screen.queryByText('Loading Rust version information.')).not.toBeInTheDocument()
    expect(screen.queryByText('Loading Oxide version information.')).not.toBeInTheDocument()

    await advancePollingTime(2_000)

    const refreshedRustSignal = api.getRustVersions.mock.calls[2][0] as AbortSignal
    const refreshedOxideSignal = api.getOxideVersions.mock.calls[2][0] as AbortSignal

    expect(staleRustSignal.aborted).toBe(true)
    expect(staleOxideSignal.aborted).toBe(true)
    expect(refreshedRustSignal.aborted).toBe(false)
    expect(refreshedOxideSignal.aborted).toBe(false)

    staleRust.resolve({
      ...rustVersions,
      installedBuildId: 11111111,
    })
    staleOxide.resolve({
      ...oxideVersions,
      installedVersion: 'stale-oxide',
    })
    await flushReactUpdates()

    expect(screen.getByRole('article', { name: 'Rust version information' })).toHaveTextContent(
      /Installed\s*33333333/,
    )
    expect(screen.getByRole('article', { name: 'Oxide version information' })).toHaveTextContent(
      /Installed\s*latest-oxide/,
    )
  })

  it('stops readiness polling when a reconciliation response is Stopped', async () => {
    vi.useFakeTimers()
    api.getServerStatus
      .mockResolvedValueOnce({ isRunning: true, isReady: false })
      .mockResolvedValueOnce(stoppedServerStatus)
    api.getRustVersions.mockResolvedValue(rustVersions)
    api.getOxideVersions.mockResolvedValue(oxideVersions)

    render(<ControlPlaneTestSurface />)
    await flushReactUpdates()

    await advancePollingTime(2_000)

    expect(screen.getByText('Rust server is stopped.')).toBeVisible()
    expect(api.getRustVersions).toHaveBeenCalledTimes(2)
    expect(api.getOxideVersions).toHaveBeenCalledTimes(2)

    await advancePollingTime(8_000)

    expect(api.getServerStatus).toHaveBeenCalledTimes(2)
  })

  it('stops readiness polling and preserves an Inconsistent response', async () => {
    vi.useFakeTimers()
    api.getServerStatus
      .mockResolvedValueOnce({ isRunning: true, isReady: false })
      .mockResolvedValueOnce({ isRunning: false, isReady: true })
    api.getRustVersions.mockResolvedValue(rustVersions)
    api.getOxideVersions.mockResolvedValue(oxideVersions)

    render(<ControlPlaneTestSurface />)
    await flushReactUpdates()

    await advancePollingTime(2_000)

    expect(screen.getByText('Inconsistent / Unavailable')).toBeVisible()
    expect(
      screen.getByText('Server reported an invalid running and ready combination.'),
    ).toBeVisible()
    expect(api.getRustVersions).toHaveBeenCalledOnce()
    expect(api.getOxideVersions).toHaveBeenCalledOnce()

    await advancePollingTime(8_000)

    expect(api.getServerStatus).toHaveBeenCalledTimes(2)
  })

  it('makes only the Server resource unavailable when readiness polling fails', async () => {
    vi.useFakeTimers()
    api.getServerStatus
      .mockResolvedValueOnce({ isRunning: true, isReady: false })
      .mockRejectedValueOnce(new Error('Control service did not respond.'))
    api.getRustVersions.mockResolvedValue(rustVersions)
    api.getOxideVersions.mockResolvedValue(oxideVersions)

    render(<ControlPlaneTestSurface />)
    await flushReactUpdates()

    await advancePollingTime(2_000)

    expect(screen.getByText('Server status unavailable.')).toBeVisible()
    expect(screen.getByText('Control service did not respond.')).toBeVisible()
    expect(screen.getByText('12345678')).toBeVisible()
    expect(screen.getByText('2.0.7725')).toBeVisible()

    await advancePollingTime(8_000)

    expect(api.getServerStatus).toHaveBeenCalledTimes(2)
  })

  it('cancels a scheduled readiness poll when Stop begins', async () => {
    vi.useFakeTimers()
    mockInitialResources({ isRunning: true, isReady: false })
    api.stopServer.mockResolvedValue(stoppedServerStatus)

    render(<ControlPlaneTestSurface />)
    await flushReactUpdates()

    fireEvent.click(screen.getByRole('button', { name: 'Stop' }))
    await flushReactUpdates()

    expect(screen.getByText('Rust server is stopped.')).toBeVisible()

    await advancePollingTime(10_000)

    expect(api.getServerStatus).toHaveBeenCalledOnce()
  })

  it('aborts an in-flight readiness poll and preserves the Stop response', async () => {
    vi.useFakeTimers()
    const poll = createDeferred<ServerStatus>()
    api.getServerStatus
      .mockResolvedValueOnce({ isRunning: true, isReady: false })
      .mockReturnValueOnce(poll.promise)
    api.getRustVersions.mockResolvedValue(rustVersions)
    api.getOxideVersions.mockResolvedValue(oxideVersions)
    api.stopServer.mockResolvedValue(stoppedServerStatus)

    render(<ControlPlaneTestSurface />)
    await flushReactUpdates()

    act(() => {
      vi.advanceTimersByTime(2_000)
    })

    expect(api.getServerStatus).toHaveBeenCalledTimes(2)
    const pollingSignal = api.getServerStatus.mock.calls[1][0] as AbortSignal
    expect(pollingSignal.aborted).toBe(false)

    fireEvent.click(screen.getByRole('button', { name: 'Stop' }))

    expect(pollingSignal.aborted).toBe(true)
    await flushReactUpdates()
    expect(screen.getByText('Rust server is stopped.')).toBeVisible()

    poll.resolve(readyServerStatus)
    await flushReactUpdates()

    expect(screen.getByText('Rust server is stopped.')).toBeVisible()

    await advancePollingTime(8_000)

    expect(api.getServerStatus).toHaveBeenCalledTimes(2)
  })

  it('does not poll while the server is Ready', async () => {
    vi.useFakeTimers()
    mockInitialResources(readyServerStatus)

    render(<ControlPlaneTestSurface />)
    await flushReactUpdates()

    await advancePollingTime(10_000)

    expect(api.getServerStatus).toHaveBeenCalledOnce()
  })

  it('does not poll while the server is Stopped', async () => {
    vi.useFakeTimers()
    mockInitialResources(stoppedServerStatus)

    render(<ControlPlaneTestSurface />)
    await flushReactUpdates()

    await advancePollingTime(10_000)

    expect(api.getServerStatus).toHaveBeenCalledOnce()
  })

  it('tracks a Rust update from acceptance through sequential polling and reconciliation', async () => {
    vi.useFakeTimers()
    const accepted = createDeferred<{ id: string; status: string }>()
    const firstLookup = createDeferred<ReturnType<typeof createOperation>>()
    const refreshedRust = {
      ...rustVersions,
      installedBuildId: rustVersions.availableBuildId,
      updateAvailable: false,
    }
    api.getServerStatus.mockResolvedValue(stoppedServerStatus)
    api.getRustVersions
      .mockResolvedValueOnce(rustVersions)
      .mockResolvedValueOnce(refreshedRust)
    api.getOxideVersions.mockResolvedValue(oxideVersions)
    api.requestRustUpdate.mockReturnValue(accepted.promise)
    api.getOperation
      .mockReturnValueOnce(firstLookup.promise)
      .mockResolvedValueOnce(createOperation('Running'))
      .mockResolvedValueOnce(createOperation('Succeeded'))

    render(<ControlPlaneTestSurface />)
    await flushReactUpdates()

    fireEvent.click(screen.getByRole('button', { name: 'Update to 12345679' }))

    expect(api.requestRustUpdate).toHaveBeenCalledOnce()
    expect(screen.getByRole('button', { name: 'Start' })).toBeDisabled()
    expect(screen.getByRole('button', { name: 'Update to 2.0.7726' })).toBeDisabled()

    accepted.resolve({
      id: 'f1ccbe0c-a923-4a38-8f3d-9c0f58b4c0d4',
      status: 'Pending',
    })
    await flushReactUpdates()

    expect(screen.getByText('Rust update accepted')).toBeVisible()
    expect(screen.getByText('Waiting for authoritative operation detail.')).toBeVisible()
    expect(api.getOperation).toHaveBeenCalledOnce()

    firstLookup.resolve(createOperation('Pending'))
    await flushReactUpdates()
    expect(screen.getByRole('article', { name: 'Operation activity' })).toHaveTextContent(
      /Status\s*Pending/,
    )

    await advancePollingTime(2_000)
    expect(api.getOperation).toHaveBeenCalledTimes(2)
    expect(screen.getByRole('article', { name: 'Operation activity' })).toHaveTextContent(
      /Status\s*Running/,
    )

    await advancePollingTime(2_000)
    expect(api.getOperation).toHaveBeenCalledTimes(3)
    await flushReactUpdates()
    expect(screen.getByRole('article', { name: 'Operation activity' })).toHaveTextContent(
      /Status\s*Succeeded/,
    )
    expect(screen.queryByRole('button', { name: 'Update to 12345679' })).not.toBeInTheDocument()

    await advancePollingTime(8_000)
    expect(api.getOperation).toHaveBeenCalledTimes(3)
    expect(api.getServerStatus).toHaveBeenCalledTimes(2)
    expect(api.getRustVersions).toHaveBeenCalledTimes(2)
    expect(api.getOxideVersions).toHaveBeenCalledTimes(2)
  })

  it('routes an Oxide update through its endpoint and reconciles the observed version', async () => {
    const updatedOxide = {
      ...oxideVersions,
      installedVersion: oxideVersions.availableVersion,
      updateAvailable: false,
    }
    mockInitialResources(stoppedServerStatus)
    api.getServerStatus.mockResolvedValue(stoppedServerStatus)
    api.getRustVersions.mockResolvedValue(rustVersions)
    api.getOxideVersions
      .mockResolvedValueOnce(oxideVersions)
      .mockResolvedValueOnce(updatedOxide)
    api.requestOxideUpdate.mockResolvedValue({
      id: 'f1ccbe0c-a923-4a38-8f3d-9c0f58b4c0d4',
      status: 'Pending',
    })
    api.getOperation.mockResolvedValue(createOperation('Succeeded', 'OxideUpdate'))

    render(<ControlPlaneTestSurface />)

    fireEvent.click(await screen.findByRole('button', { name: 'Update to 2.0.7726' }))
    expect(api.requestOxideUpdate).toHaveBeenCalledOnce()
    expect(await screen.findByRole('article', { name: 'Operation activity' })).toHaveTextContent(
      /Type\s*Oxide update/,
    )
    expect(
      screen.getByRole('article', { name: 'Oxide version information' }),
    ).toHaveTextContent(/Installed\s*2\.0\.7726/)
    expect(api.startServer).not.toHaveBeenCalled()
    expect(api.restartServer).not.toHaveBeenCalled()
  })

  it('keeps a failed operation visible, including optional failure details, then reconciles', async () => {
    mockInitialResources(stoppedServerStatus)
    api.getServerStatus.mockResolvedValue(stoppedServerStatus)
    api.getRustVersions.mockResolvedValue(rustVersions)
    api.getOxideVersions.mockResolvedValue(oxideVersions)
    api.requestRustUpdate.mockResolvedValue({
      id: 'f1ccbe0c-a923-4a38-8f3d-9c0f58b4c0d4',
      status: 'Pending',
    })
    api.getOperation.mockResolvedValue(
      createOperation('Failed', 'RustUpdate', {
        error: 'SteamCMD exited with code 1.',
        exitCode: 1,
        finishedAt: '2026-09-29T08:31:00Z',
      }),
    )

    render(<ControlPlaneTestSurface />)
    fireEvent.click(await screen.findByRole('button', { name: 'Update to 12345679' }))

    const activity = await screen.findByRole('article', { name: 'Operation activity' })
    expect(activity).toHaveTextContent(/Status\s*Failed/)
    expect(activity).toHaveTextContent(/Exit code\s*1/)
    expect(activity).toHaveTextContent('SteamCMD exited with code 1.')
    expect(api.getServerStatus).toHaveBeenCalledTimes(2)
    expect(api.getRustVersions).toHaveBeenCalledTimes(2)
    expect(api.getOxideVersions).toHaveBeenCalledTimes(2)
    expect(api.startServer).not.toHaveBeenCalled()
  })

  it('keeps a Rust update conflict local and reconciles observable resources', async () => {
    mockInitialResources(stoppedServerStatus)
    api.getServerStatus.mockResolvedValue(stoppedServerStatus)
    api.getRustVersions.mockResolvedValue(rustVersions)
    api.getOxideVersions.mockResolvedValue(oxideVersions)
    api.requestRustUpdate.mockRejectedValue(
      new HttpError({
        method: 'POST',
        url: '/api/server/update',
        status: 409,
        statusText: 'Conflict',
        problemDetails: {
          detail: 'A server-mutating operation is already active.',
        },
      }),
    )

    render(<ControlPlaneTestSurface />)
    fireEvent.click(await screen.findByRole('button', { name: 'Update to 12345679' }))

    expect(
      await screen.findByText('A server-mutating operation is already active.'),
    ).toBeVisible()
    expect(screen.getByText('12345678')).toBeVisible()
    expect(screen.getByText('No known operation.')).toBeVisible()
    expect(api.getOperation).not.toHaveBeenCalled()
    expect(api.getServerStatus).toHaveBeenCalledTimes(2)
    expect(api.getRustVersions).toHaveBeenCalledTimes(2)
    expect(api.getOxideVersions).toHaveBeenCalledTimes(2)
  })

  it('treats an operation lookup 404 as unavailable and reconciles without inventing a result', async () => {
    mockInitialResources(stoppedServerStatus)
    api.getServerStatus.mockResolvedValue(stoppedServerStatus)
    api.getRustVersions.mockResolvedValue(rustVersions)
    api.getOxideVersions.mockResolvedValue(oxideVersions)
    api.requestRustUpdate.mockResolvedValue({
      id: 'f1ccbe0c-a923-4a38-8f3d-9c0f58b4c0d4',
      status: 'Pending',
    })
    api.getOperation.mockRejectedValue(
      new HttpError({
        method: 'GET',
        url: '/api/operations/f1ccbe0c-a923-4a38-8f3d-9c0f58b4c0d4',
        status: 404,
        statusText: 'Not Found',
      }),
    )

    render(<ControlPlaneTestSurface />)
    fireEvent.click(await screen.findByRole('button', { name: 'Update to 12345679' }))

    const activity = await screen.findByRole('article', { name: 'Operation activity' })
    expect(activity).toHaveTextContent('Operation detail unavailable.')
    expect(activity).toHaveTextContent('f1ccbe0c-a923-4a38-8f3d-9c0f58b4c0d4')
    expect(activity).not.toHaveTextContent('Succeeded')
    expect(activity).not.toHaveTextContent('Failed')
    expect(api.getServerStatus).toHaveBeenCalledTimes(2)
    expect(api.getRustVersions).toHaveBeenCalledTimes(2)
    expect(api.getOxideVersions).toHaveBeenCalledTimes(2)
  })

  it('keeps an unknown operation status blocked and polls it again sequentially', async () => {
    vi.useFakeTimers()
    const firstLookup = createDeferred<ReturnType<typeof createOperation>>()
    mockInitialResources(stoppedServerStatus)
    api.requestRustUpdate.mockResolvedValue({
      id: 'f1ccbe0c-a923-4a38-8f3d-9c0f58b4c0d4',
      status: 'Pending',
    })
    api.getOperation
      .mockReturnValueOnce(firstLookup.promise)
      .mockResolvedValueOnce(createOperation('Pausing'))

    render(<ControlPlaneTestSurface />)
    await flushReactUpdates()
    fireEvent.click(screen.getByRole('button', { name: 'Update to 12345679' }))
    await flushReactUpdates()

    await advancePollingTime(10_000)
    expect(api.getOperation).toHaveBeenCalledOnce()

    firstLookup.resolve(createOperation('Pausing'))
    await flushReactUpdates()
    expect(screen.getByRole('article', { name: 'Operation activity' })).toHaveTextContent(
      'Unknown status (Pausing)',
    )
    expect(screen.getByRole('button', { name: 'Start' })).toBeDisabled()

    await advancePollingTime(2_000)
    expect(api.getOperation).toHaveBeenCalledTimes(2)
  })

  it('uses one mutation gate for lifecycle and update POST requests', async () => {
    const restart = createDeferred<ServerStatus>()
    const rustUpdate = createDeferred<{ id: string; status: string }>()
    mockInitialResources(readyServerStatus)
    api.restartServer.mockReturnValue(restart.promise)

    const { rerender } = render(<ControlPlaneTestSurface />)
    fireEvent.click(await screen.findByRole('button', { name: 'Restart' }))
    expect(screen.getByRole('button', { name: 'Update to 12345679' })).toBeDisabled()
    fireEvent.click(screen.getByRole('button', { name: 'Update to 12345679' }))
    expect(api.requestRustUpdate).not.toHaveBeenCalled()

    restart.resolve(stoppedServerStatus)
    await flushReactUpdates()
    api.requestRustUpdate.mockReturnValue(rustUpdate.promise)
    rerender(<ControlPlaneTestSurface />)

    fireEvent.click(screen.getByRole('button', { name: 'Update to 12345679' }))
    expect(screen.getByRole('button', { name: 'Start' })).toBeDisabled()
    fireEvent.click(screen.getByRole('button', { name: 'Start' }))
    expect(api.startServer).not.toHaveBeenCalled()
    expect(screen.getByRole('button', { name: 'Update to 2.0.7726' })).toBeDisabled()
    expect(api.requestOxideUpdate).not.toHaveBeenCalled()
  })

  it('aborts an abandoned operation poll and ignores its late response', async () => {
    const lookup = createDeferred<ReturnType<typeof createOperation>>()
    mockInitialResources(stoppedServerStatus)
    api.requestRustUpdate.mockResolvedValue({
      id: 'f1ccbe0c-a923-4a38-8f3d-9c0f58b4c0d4',
      status: 'Pending',
    })
    api.getOperation.mockReturnValue(lookup.promise)

    const { unmount } = render(<ControlPlaneTestSurface />)
    fireEvent.click(await screen.findByRole('button', { name: 'Update to 12345679' }))
    await flushReactUpdates()

    const pollingSignal = api.getOperation.mock.calls[0][1] as AbortSignal
    expect(pollingSignal.aborted).toBe(false)
    unmount()
    expect(pollingSignal.aborted).toBe(true)

    lookup.resolve(createOperation('Succeeded'))
    await flushReactUpdates()
    expect(api.getServerStatus).toHaveBeenCalledOnce()
  })
})
