import { strFromU8, strToU8, unzipSync, zipSync } from 'fflate'
import type { LibraryPlugin, PrototypeLibrary } from './model'

const format = 'rustserver.plugin-catalog.backup'
const version = 1
const maxArchive = 20 * 1024 * 1024
const maxExpanded = 24 * 1024 * 1024
type Role = 'Icon' | 'Preview' | 'Technical'
type MediaEntry = { pluginId: string; role: Role; index: number; path: string; mimeType: string; sha256: string }
type MissingEntry = { pluginId: string; role: Role; index: number; reference: string }
type Manifest = { format: string; formatVersion: number; createdAt: string; catalogSha256: string; media: MediaEntry[]; unavailableMedia: MissingEntry[]; externalMediaCount: number }
export type BackupPreview = { library: PrototypeLibrary; manifest: Manifest }
const record = (value: unknown): value is Record<string, unknown> => !!value && typeof value === 'object' && !Array.isArray(value)
const strings = (value: unknown): value is string[] => Array.isArray(value) && value.every(item => typeof item === 'string')
const nonempty = (value: unknown): value is string => typeof value === 'string' && !!value.trim()
const allowedMime = new Set(['image/png', 'image/jpeg', 'image/gif', 'image/webp'])
async function sha256(bytes: Uint8Array): Promise<string> {
  const digest = await crypto.subtle.digest('SHA-256', new Uint8Array(bytes))
  return [...new Uint8Array(digest)].map(byte => byte.toString(16).padStart(2, '0')).join('')
}

function mediaSlots(plugin: LibraryPlugin): { role: Role; index: number; value: string; set: (value: string) => void }[] {
  return [
    ...(plugin.iconUrl ? [{ role: 'Icon' as const, index: 0, value: plugin.iconUrl, set: (value: string) => { plugin.iconUrl = value } }] : []),
    ...(plugin.previews ?? []).map((value, index) => ({ role: 'Preview' as const, index, value, set: (url: string) => { plugin.previews![index] = url } })),
    ...(plugin.technicalMedia ?? []).map((value, index) => ({ role: 'Technical' as const, index, value, set: (url: string) => { plugin.technicalMedia![index] = url } })),
  ]
}

export async function createBackup(library: PrototypeLibrary): Promise<{ bytes: Uint8Array; manifest: Manifest }> {
  const catalog = structuredClone(library)
  const files: Record<string, Uint8Array> = { 'media/': new Uint8Array() }
  const manifest: Manifest = { format, formatVersion: version, createdAt: new Date().toISOString(), catalogSha256: '', media: [], unavailableMedia: [], externalMediaCount: 0 }
  let totalBytes = 0
  for (const plugin of catalog.plugins) for (const slot of mediaSlots(plugin)) {
    if (/^https?:\/\//i.test(slot.value)) { manifest.externalMediaCount++; continue }
    if (/^(data:image\/|blob:)/i.test(slot.value)) {
      try {
        const response = await fetch(slot.value)
        if (!response.ok) throw new Error('unavailable')
        const blob = await response.blob()
        if (!allowedMime.has(blob.type.toLowerCase()) || !blob.size) throw new Error('unsupported image')
        totalBytes += blob.size
        if (totalBytes > maxExpanded) throw new Error('limit')
        const path = `media/${String(manifest.media.length + 1).padStart(6, '0')}`
        files[path] = new Uint8Array(await blob.arrayBuffer())
        manifest.media.push({ pluginId: plugin.id, role: slot.role, index: slot.index, path, mimeType: blob.type.toLowerCase(), sha256: await sha256(files[path]) })
        slot.set(`archive:${path}`)
        continue
      } catch (error) { if (error instanceof Error && error.message === 'limit') throw new Error('Local media exceeds the prototype backup size limit (24 MiB).') }
    }
    manifest.unavailableMedia.push({ pluginId: plugin.id, role: slot.role, index: slot.index, reference: slot.value })
  }
  files['catalog.json'] = strToU8(JSON.stringify(catalog, null, 2))
  manifest.catalogSha256 = await sha256(files['catalog.json'])
  files['manifest.json'] = strToU8(JSON.stringify(manifest, null, 2))
  if (totalBytes + files['catalog.json'].length + files['manifest.json'].length > maxExpanded) throw new Error('Expanded backup exceeds the prototype size limit (24 MiB).')
  const bytes = zipSync(files, { level: 6 })
  if (bytes.length > maxArchive) throw new Error('Backup exceeds the prototype archive size limit (20 MiB).')
  return { bytes, manifest }
}

function validateLibrary(value: unknown): asserts value is PrototypeLibrary {
  if (!record(value) || value.version !== 1 || !Array.isArray(value.plugins) || !Array.isArray(value.tags) || !strings(value.folders) || !strings(value.sites) || !Array.isArray(value.views) || !record(value.presetTags)) throw new Error('Invalid catalog structure.')
  const tags = value.tags
  const folders = value.folders, sites = value.sites
  if (!tags.every(tag => record(tag) && nonempty(tag.name) && nonempty(tag.color))) throw new Error('Invalid library tags.')
  const names = tags.map(tag => tag.name as string)
  const unique = (items: string[]) => new Set(items.map(item => item.toLowerCase())).size === items.length
  if (!unique(names) || !value.folders.every(nonempty) || !unique(value.folders) || !value.sites.every(nonempty) || !unique(value.sites)) throw new Error('Duplicate or empty library items.')
  const preset = value.presetTags
  if (typeof preset.purchased !== 'string' || typeof preset.archived !== 'string' || !strings(preset.wishlist)) throw new Error('Invalid organization settings.')
  if (!value.views.every(view => record(view) && nonempty(view.name) && typeof view.site === 'string' && typeof view.tag === 'string' && typeof view.folder === 'string' && (!view.site || sites.includes(view.site)) && (!view.tag || names.includes(view.tag)) && (!view.folder || folders.includes(view.folder)))) throw new Error('Invalid saved views or references.')
  const ids = new Set<string>(), slugs = new Set<string>()
  for (const plugin of value.plugins) {
    if (!record(plugin) || !nonempty(plugin.id) || !nonempty(plugin.slug) || !nonempty(plugin.name) || ids.has(plugin.id) || slugs.has(plugin.slug)) throw new Error('Invalid or duplicate plugin identity.')
    ids.add(plugin.id); slugs.add(plugin.slug)
    for (const key of ['author', 'description', 'sourceDescription', 'lastUpdate', 'notes', 'iconUrl']) if (plugin[key] !== undefined && typeof plugin[key] !== 'string') throw new Error(`Invalid plugin field ${key}.`)
    if ((plugin.previews !== undefined && !strings(plugin.previews)) || (plugin.technicalMedia !== undefined && !strings(plugin.technicalMedia)) || !strings(plugin.folders) || !plugin.folders.every(folder => folders.includes(folder)) || !Array.isArray(plugin.tags) || !plugin.tags.every(tag => record(tag) && typeof tag.name === 'string' && typeof tag.color === 'string' && tags.some(known => known.name === tag.name && known.color === tag.color))) throw new Error('Invalid plugin media, tags or folder references.')
    if (!Array.isArray(plugin.sources) || !plugin.sources.every(source => record(source) && typeof source.url === 'string' && typeof source.site === 'string' && (!source.site || sites.includes(source.site)))) throw new Error('Invalid source references.')
    if (plugin.price !== undefined && (!record(plugin.price) || typeof plugin.price.amount !== 'number' || !Number.isFinite(plugin.price.amount) || plugin.price.amount < 0 || typeof plugin.price.currency !== 'string' || !/^[a-zA-Z]{3}$/.test(plugin.price.currency))) throw new Error('Invalid price.')
  }
}

function asDataUrl(bytes: Uint8Array, mime: string): string {
  let binary = ''
  for (let i = 0; i < bytes.length; i += 0x8000) binary += String.fromCharCode(...bytes.subarray(i, i + 0x8000))
  return `data:${mime};base64,${btoa(binary)}`
}

export async function readBackup(file: File): Promise<BackupPreview> {
  if (file.size > maxArchive) throw new Error('Archive exceeds 20 MiB.')
  const input = new Uint8Array(await file.arrayBuffer())
  let total = 0
  const paths = new Set<string>()
  const files = unzipSync(input, { filter: entry => {
    if (paths.size >= 10000 || paths.has(entry.name) || !/^(manifest\.json|catalog\.json|media\/|media\/\d{6})$/.test(entry.name)) throw new Error('Unexpected, duplicate or too many archive entries.')
    paths.add(entry.name); total += entry.originalSize
    if (total > maxExpanded || entry.originalSize > maxExpanded) throw new Error('Expanded archive exceeds 24 MiB.')
    return true
  } })
  if (!files['manifest.json'] || !files['catalog.json']) throw new Error('Missing manifest.json or catalog.json.')
  const manifest: unknown = JSON.parse(strFromU8(files['manifest.json']))
  const library: unknown = JSON.parse(strFromU8(files['catalog.json']))
  if (!record(manifest) || manifest.format !== format || manifest.formatVersion !== version || typeof manifest.createdAt !== 'string' || typeof manifest.catalogSha256 !== 'string' || manifest.catalogSha256 !== await sha256(files['catalog.json']) || !Array.isArray(manifest.media) || !Array.isArray(manifest.unavailableMedia) || !Number.isInteger(manifest.externalMediaCount) || (manifest.externalMediaCount as number) < 0) throw new Error('Unsupported, invalid or damaged backup manifest or catalog.')
  validateLibrary(library)
  const linked = new Set<string>()
  const slotsAccountedFor = new Set<string>()
  const slotKey = (id: string, role: string, index: number) => JSON.stringify([id, role, index])
  for (const raw of manifest.media) {
    if (!record(raw) || typeof raw.pluginId !== 'string' || !['Icon', 'Preview', 'Technical'].includes(raw.role as string) || !Number.isInteger(raw.index) || (raw.index as number) < 0 || typeof raw.path !== 'string' || !/^media\/\d{6}$/.test(raw.path) || typeof raw.mimeType !== 'string' || !allowedMime.has(raw.mimeType) || linked.has(raw.path) || !files[raw.path]?.length || typeof raw.sha256 !== 'string' || raw.sha256 !== await sha256(files[raw.path])) throw new Error('Invalid, damaged or missing media entry.')
    const plugin = library.plugins.find(item => item.id === raw.pluginId)
    const slot = plugin && mediaSlots(plugin).find(item => item.role === raw.role && item.index === raw.index)
    if (!slot || slot.value !== `archive:${raw.path}`) throw new Error('Media association does not match the catalog.')
    const key = slotKey(raw.pluginId, raw.role as string, raw.index as number)
    if (slotsAccountedFor.has(key)) throw new Error('Duplicate media association.')
    slotsAccountedFor.add(key); linked.add(raw.path); slot.set(asDataUrl(files[raw.path], raw.mimeType))
  }
  if ([...paths].some(path => path.startsWith('media/') && path !== 'media/' && !linked.has(path)) || library.plugins.some(plugin => mediaSlots(plugin).some(slot => slot.value.startsWith('archive:')))) throw new Error('Unmatched media in archive.')
  for (const raw of manifest.unavailableMedia) {
    if (!record(raw) || typeof raw.pluginId !== 'string' || !['Icon', 'Preview', 'Technical'].includes(raw.role as string) || !Number.isInteger(raw.index) || typeof raw.reference !== 'string' || /^https?:\/\//i.test(raw.reference) || !library.plugins.some(plugin => plugin.id === raw.pluginId && mediaSlots(plugin).some(slot => slot.role === raw.role && slot.index === raw.index && slot.value === raw.reference))) throw new Error('Invalid unavailable media references.')
    const key = slotKey(raw.pluginId, raw.role as string, raw.index as number)
    if (slotsAccountedFor.has(key)) throw new Error('Duplicate media association.')
    slotsAccountedFor.add(key)
  }
  let externalCount = 0
  for (const plugin of library.plugins) for (const slot of mediaSlots(plugin)) {
    if (/^https?:\/\//i.test(slot.value)) externalCount++
    else if (!slotsAccountedFor.has(slotKey(plugin.id, slot.role, slot.index))) throw new Error('Unreported local media reference.')
  }
  if (externalCount !== manifest.externalMediaCount) throw new Error('External media count does not match the catalog.')
  return { library, manifest: manifest as Manifest }
}
