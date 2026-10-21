import { useState } from 'react'
import type { Meta, StoryObj } from '@storybook/react-vite'
import { initialLibrary } from './library'
import { ImportDialog } from './ImportDialog'
import { PluginDetails } from './PluginDetails'
import { BackupDialog } from './BackupDialog'

function Demo({ mode }: { mode: 'details' | 'import' | 'export' | 'restore' }) {
  const [library, setLibrary] = useState(initialLibrary)
  const [open, setOpen] = useState(true)
  return !open ? <button onClick={() => setOpen(true)}>Open</button> : mode === 'details'
    ? <PluginDetails plugin={library.plugins[5]} library={library} onSave={plugin => setLibrary({ ...library, plugins: library.plugins.map(item => item.id === plugin.id ? plugin : item) })} onBack={() => setOpen(false)} />
    : mode === 'import' ? <ImportDialog library={library} onImport={setLibrary} onClose={() => setOpen(false)} />
    : <BackupDialog mode={mode} library={library} onRestore={next => { setLibrary(next); return true }} onClose={() => setOpen(false)} />
}
const meta = { title: 'Plugin Catalog Prototype/Details and Import', component: Demo, parameters: { layout: 'fullscreen' } } satisfies Meta<typeof Demo>
export default meta
type Story = StoryObj<typeof meta>
export const PluginDetailsPage: Story = { args: { mode: 'details' } }
export const JsonImport: Story = { args: { mode: 'import' } }
export const ExportCatalog: Story = { args: { mode: 'export' } }
export const RestoreCatalog: Story = { args: { mode: 'restore' } }
