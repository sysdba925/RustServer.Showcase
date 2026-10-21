import { useState } from 'react'
import type { Meta, StoryObj } from '@storybook/react-vite'
import { initialLibrary } from './library'
import { LibraryManager } from './LibraryManager'
import { PluginOrganizer } from './PluginOrganizer'

function OrganizationDemo({ mode }: { mode: 'manage' | 'organize' }) {
  const [library, setLibrary] = useState(initialLibrary)
  const [open, setOpen] = useState(true)
  const plugin = library.plugins[2]
  return <>{!open && <button type="button" onClick={() => setOpen(true)}>Open</button>}{open && (mode === 'manage'
    ? <LibraryManager library={library} onChange={setLibrary} onClose={() => setOpen(false)} />
    : <PluginOrganizer library={library} plugin={plugin} onChange={updated => setLibrary({ ...library, plugins: library.plugins.map(item => item.name === updated.name ? updated : item) })} onClose={() => setOpen(false)} />)}</>
}

const meta = { title: 'Plugin Catalog Prototype/Organization', component: OrganizationDemo, parameters: { layout: 'fullscreen' } } satisfies Meta<typeof OrganizationDemo>
export default meta
type Story = StoryObj<typeof meta>
export const ManageLibrary: Story = { args: { mode: 'manage' } }
export const OrganizePlugin: Story = { args: { mode: 'organize' } }
