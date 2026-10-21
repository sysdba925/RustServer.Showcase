import type { Meta, StoryObj } from '@storybook/react-vite'

import type { GetPluginsOperationResponse, GetPluginsOperationsResponse } from '../../../shared/api'
import { succeededPluginsOperation } from '../fixtures/plugins-api-scenarios'

import { OperationHistoryDialog } from './OperationHistoryDialog'

type Story = StoryObj<typeof OperationHistoryDialog>

const historyPage: GetPluginsOperationsResponse = {
  items: [
    {
      operationId: '8b2ca269-e9df-496c-977a-9d85c139b1a1',
      type: 'gitSync',
      status: 'succeeded',
      startedAt: '2026-10-10T10:15:00Z',
      completedAt: '2026-10-10T10:15:03Z',
      changedFiles: 0,
    },
    {
      operationId: '6ff3131e-c21f-45f0-839c-f983755ea3bd',
      type: 'commitRuntime',
      status: 'interrupted',
      startedAt: '2026-10-10T10:10:00Z',
      completedAt: '2026-10-10T10:10:03Z',
      changedFiles: 2,
    },
  ],
  nextCursor: null,
}

const interruptedReport: GetPluginsOperationResponse = {
  ...succeededPluginsOperation,
  operationId: historyPage.items[1].operationId,
  type: 'commitRuntime',
  status: 'interrupted',
  completedAt: '2026-10-10T10:10:03Z',
  failure: {
    phase: 'captureFinalState',
    code: 'OperationInterrupted',
    message: 'The service restarted before the operation completed.',
    repository: null,
    filePath: null,
  },
}

const meta = {
  title: 'Plugins/Operation History Dialog',
  component: OperationHistoryDialog,
  args: {
    getOperation: async (operationId) => operationId === interruptedReport.operationId
      ? interruptedReport
      : succeededPluginsOperation,
    getOperations: async () => historyPage,
    onClose: () => undefined,
  },
} satisfies Meta<typeof OperationHistoryDialog>

export default meta

export const Summaries: Story = {}

export const Empty: Story = {
  args: {
    getOperations: async () => ({ items: [], nextCursor: null }),
  },
}

export const Unavailable: Story = {
  args: {
    getOperations: async () => {
      throw new Error('Operation history storage is unavailable.')
    },
  },
}
