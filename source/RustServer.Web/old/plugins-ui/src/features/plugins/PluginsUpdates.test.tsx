import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { createMemoryRouter, RouterProvider } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import { PluginsUpdatePackagePage } from './PluginsUpdatePackagePage'
import { PluginsUpdatesPage } from './PluginsUpdatesPage'

const api = vi.hoisted(() => ({
  HttpError: class HttpError extends Error {
    status: number
    problemDetails: { detail?: string } | undefined
    constructor(status: number, detail?: string) { super(`HTTP ${status}`); this.status = status; this.problemDetails = detail ? { detail } : undefined }
  },
  uploadUpdatePackage: vi.fn(),
  getUpdatePackage: vi.fn(),
  getIncomingChanges: vi.fn(),
  getIncomingChangeDiff: vi.fn(),
  commitUpdatePackage: vi.fn(),
}))

vi.mock('../../shared/api', () => api)

const inspectedPackage = {
  id: 'package-1', originalFileName: 'Example.zip', status: 'inspected' as const, createdAt: '2026-10-01T10:00:00Z',
  files: [{ path: 'oxide/plugins/Example.cs', classification: 'managed' as const }], commits: [],
}

const incomingChanges = {
  packageId: 'package-1',
  workspaceHeads: [{ repository: 'plugins' as const, head: 'plugins-head' }, { repository: 'oxideConfigs' as const, head: 'config-head' }],
  changes: [
    { path: 'oxide/plugins/Example.cs', repository: 'plugins' as const, classification: 'managed' as const, changeType: 'modified' as const, textDiffAvailable: true },
    { path: 'oxide/config/Example.json', repository: 'oxideConfigs' as const, classification: 'managed' as const, changeType: 'unchanged' as const, textDiffAvailable: true },
    { path: 'oxide/data/runtime.json', repository: 'oxideConfigs' as const, classification: 'runtime' as const, changeType: 'modified' as const, textDiffAvailable: false },
    { path: 'oxide/logs/Example.log', repository: 'oxideConfigs' as const, classification: 'artifact' as const, changeType: 'added' as const, textDiffAvailable: false },
    { path: 'oxide/ignored/Example.txt', repository: 'oxideConfigs' as const, classification: 'ignored' as const, changeType: 'added' as const, textDiffAvailable: false },
  ],
}

beforeEach(() => {
  vi.clearAllMocks()
  api.getUpdatePackage.mockResolvedValue(inspectedPackage)
  api.getIncomingChanges.mockResolvedValue(incomingChanges)
  api.getIncomingChangeDiff.mockResolvedValue({ packageId: 'package-1', path: 'oxide/plugins/Example.cs', repository: 'plugins', changeType: 'modified', workspaceHead: 'plugins-head', workspaceText: null, incomingText: 'class Example {}' })
  api.commitUpdatePackage.mockResolvedValue({ packageId: 'package-1', commits: [{ repository: 'plugins', previousHead: 'plugins-head', commitSha: 'new-plugins-head', committedPaths: ['oxide/plugins/Example.cs'] }, { repository: 'oxideConfigs', previousHead: 'config-head', commitSha: 'new-config-head', committedPaths: ['oxide/config/Example.json'] }] })
})

function renderPackage(path = '/plugins/updates/package-1') {
  const router = createMemoryRouter([{ path: '/plugins/updates/:packageId', element: <PluginsUpdatePackagePage /> }], { initialEntries: [path] })
  return render(<RouterProvider router={router} />)
}

describe('Plugins Updates', () => {
  it('uploads multipart input once and navigates to the server-owned package ID', async () => {
    api.uploadUpdatePackage.mockResolvedValue(inspectedPackage)
    const router = createMemoryRouter([
      { path: '/plugins/updates', element: <PluginsUpdatesPage /> },
      { path: '/plugins/updates/:packageId', element: <p>Package destination</p> },
    ], { initialEntries: ['/plugins/updates'] })
    render(<RouterProvider router={router} />)

    const input = screen.getByLabelText('Update source or package')
    fireEvent.change(input, { target: { files: [new File(['source'], 'Example.cs', { type: 'text/plain' })] } })
    const upload = screen.getByRole('button', { name: 'Upload Update' })
    fireEvent.click(upload)
    fireEvent.click(upload)

    expect(await screen.findByText('Package destination')).toBeVisible()
    expect(api.uploadUpdatePackage).toHaveBeenCalledTimes(1)
    expect(api.uploadUpdatePackage).toHaveBeenCalledWith(expect.objectContaining({ name: 'Example.cs' }))
  })

  it('shows an upload failure', async () => {
    api.uploadUpdatePackage.mockRejectedValue(new api.HttpError(400, 'Only .zip or .cs is accepted.'))
    const router = createMemoryRouter([{ path: '/plugins/updates', element: <PluginsUpdatesPage /> }], { initialEntries: ['/plugins/updates'] })
    render(<RouterProvider router={router} />)
    fireEvent.change(screen.getByLabelText('Update source or package'), { target: { files: [new File(['x'], 'bad.exe')] } })
    fireEvent.click(screen.getByRole('button', { name: 'Upload Update' }))
    expect(await screen.findByRole('alert')).toHaveTextContent('Only .zip or .cs is accepted.')
  })

  it('loads an inspected package from its reloadable URL and selects only changed Managed files', async () => {
    renderPackage()
    expect(await screen.findByText('Example.zip')).toBeVisible()
    expect(api.getUpdatePackage).toHaveBeenCalledWith('package-1')
    expect(screen.getByText('runtime')).toBeVisible()
    expect(screen.getByText('artifact')).toBeVisible()
    expect(screen.getByText('ignored')).toBeVisible()
    expect(screen.getByLabelText('Accept oxide/plugins/Example.cs')).toBeEnabled()
    expect(screen.getByLabelText('Accept oxide/config/Example.json')).toBeDisabled()
    expect(screen.getByLabelText('Accept oxide/data/runtime.json')).toBeDisabled()
    fireEvent.click(screen.getByRole('button', { name: 'Select all changed managed files' }))
    expect(screen.getByLabelText('Accept oxide/plugins/Example.cs')).toBeChecked()
    expect(screen.getByLabelText('Accept oxide/config/Example.json')).not.toBeChecked()
  })

  it('uses comparison heads for a text diff and handles added-file null workspace text', async () => {
    renderPackage()
    fireEvent.click((await screen.findAllByRole('button', { name: 'View diff' }))[0])
    expect(await screen.findByRole('heading', { name: 'Incoming file diff' })).toBeVisible()
    expect(api.getIncomingChangeDiff).toHaveBeenCalledWith('package-1', 'oxide/plugins/Example.cs', 'plugins-head')
  })

  it('does not request a diff when the backend marks text diff unavailable', async () => {
    renderPackage()
    await screen.findAllByText('Text diff unavailable')
    expect(api.getIncomingChangeDiff).not.toHaveBeenCalled()
  })

  it('commits exactly selected paths with current heads and presents multiple repository commits', async () => {
    renderPackage()
    fireEvent.click(await screen.findByRole('button', { name: 'Select all changed managed files' }))
    fireEvent.change(screen.getByLabelText('Commit message'), { target: { value: 'Apply Example update' } })
    fireEvent.click(screen.getByRole('button', { name: 'Commit UpdatePackage' }))
    await waitFor(() => expect(api.commitUpdatePackage).toHaveBeenCalledWith('package-1', {
      acceptedPaths: ['oxide/plugins/Example.cs'],
      expectedWorkspaceHeads: { plugins: 'plugins-head', oxideConfigs: 'config-head' },
      message: 'Apply Example update',
    }))
    expect(await screen.findByText('Update package committed')).toBeVisible()
    expect(screen.getByText('new-plugins-head')).toBeVisible()
    expect(screen.getByText('new-config-head')).toBeVisible()
  })

  it('handles an unknown package ID truthfully', async () => {
    api.getUpdatePackage.mockRejectedValue(new api.HttpError(404))
    renderPackage('/plugins/updates/missing')
    expect(await screen.findByRole('alert')).toHaveTextContent('This update package is no longer available or the ID is unknown.')
  })

  it('clears stale selection and closes the diff when a concurrency conflict occurs', async () => {
    api.getIncomingChangeDiff.mockRejectedValue(new api.HttpError(409, 'Workspace head changed.'))
    renderPackage()
    fireEvent.click(await screen.findByRole('button', { name: 'Select all changed managed files' }))
    fireEvent.click(screen.getAllByRole('button', { name: 'View diff' })[0])
    await waitFor(() => expect(api.getIncomingChanges).toHaveBeenCalledTimes(2))
    expect(screen.getByLabelText('Accept oxide/plugins/Example.cs')).not.toBeChecked()
    expect(screen.queryByRole('heading', { name: 'Incoming file diff' })).not.toBeInTheDocument()
    expect(screen.getByRole('alert')).toHaveTextContent('Workspace head changed.')
  })
})
