import { getPrototypePlugins } from './data'
import type { LibraryPlugin, LibraryTag, PrototypeLibrary } from './model'

const storageKey = 'rustserver.catalog-prototype.library.v1'
const palette = ['#7f9abd', '#c49a60', '#8da96b', '#b98280', '#b59aca', '#80b8ad']

export function uniqueSlug(name: string, plugins: LibraryPlugin[], ignoreId?: string): string {
  const base = name.toLowerCase().normalize('NFKD').replace(/\p{M}/gu, '').replace(/[^\p{L}\p{N}]+/gu, '-').replace(/^-|-$/g, '') || 'plugin'
  let slug = base
  let suffix = 2
  while (plugins.some(plugin => plugin.id !== ignoreId && plugin.slug === slug)) slug = `${base}-${suffix++}`
  return slug
}

export function makePlugin(name: string, plugins: LibraryPlugin[]): LibraryPlugin {
  return { id: crypto.randomUUID(), slug: uniqueSlug(name, plugins), name, sources: [], tags: [], folders: [] }
}

export function initialLibrary(): PrototypeLibrary {
  const plugins = structuredClone(getPrototypePlugins()) as PrototypeLibrary['plugins']
  return {
    version: 1, plugins,
    tags: [...new Map(plugins.flatMap(plugin => plugin.tags).map(tag => [tag.name, tag])).values()],
    folders: [...new Set(plugins.flatMap(plugin => plugin.folders))],
    sites: [...new Set(plugins.flatMap(plugin => plugin.sources.map(source => source.site)))],
    views: [],
    presetTags: { purchased: 'Purchased', wishlist: ['Nice to have', 'Planned purchase'], archived: 'Archived' },
  }
}

export function loadLibrary(): PrototypeLibrary {
  try {
    const saved = localStorage.getItem(storageKey)
    if (saved) {
      const value = JSON.parse(saved) as PrototypeLibrary
      if (value.version === 1 && Array.isArray(value.plugins) && Array.isArray(value.tags) && Array.isArray(value.folders) && Array.isArray(value.sites) && Array.isArray(value.views) && value.presetTags) {
        const migrated: LibraryPlugin[] = []
        for (const plugin of value.plugins) migrated.push({ ...plugin, id: plugin.id || crypto.randomUUID(), slug: plugin.slug || uniqueSlug(plugin.name, migrated) })
        return { ...value, plugins: migrated }
      }
    }
  } catch { /* Start with the fixture if storage is unavailable or invalid. */ }
  return initialLibrary()
}

export function saveLibrary(library: PrototypeLibrary): boolean {
  try { localStorage.setItem(storageKey, JSON.stringify(library)); return true } catch { return false }
}

export function createTag(name: string, tags: LibraryTag[]): LibraryTag {
  return { name, color: palette[tags.length % palette.length] }
}

export type Collection = 'folders' | 'tags' | 'sites'

// Rename or remove references together with a managed library item.
export function updateCollection(library: PrototypeLibrary, kind: Collection, oldName: string, newName: string | null): PrototypeLibrary {
  const replace = (names: string[]) => names.flatMap(name => name === oldName ? (newName ? [newName] : []) : [name])
  const filters = (view: PrototypeLibrary['views'][number]) => ({ ...view, [kind === 'folders' ? 'folder' : kind === 'tags' ? 'tag' : 'site']: view[kind === 'folders' ? 'folder' : kind === 'tags' ? 'tag' : 'site'] === oldName ? newName ?? '' : view[kind === 'folders' ? 'folder' : kind === 'tags' ? 'tag' : 'site'] })
  return {
    ...library,
    folders: kind === 'folders' ? replace(library.folders) : library.folders,
    sites: kind === 'sites' ? replace(library.sites) : library.sites,
    tags: kind === 'tags' ? library.tags.flatMap(tag => tag.name === oldName ? (newName ? [{ ...tag, name: newName }] : []) : [tag]) : library.tags,
    plugins: library.plugins.map(plugin => ({ ...plugin,
      folders: kind === 'folders' ? replace(plugin.folders) : plugin.folders,
      tags: kind === 'tags' ? plugin.tags.flatMap(tag => tag.name === oldName ? (newName ? [{ ...tag, name: newName }] : []) : [tag]) : plugin.tags,
      sources: kind === 'sites' ? plugin.sources.flatMap(source => source.site === oldName ? (newName ? [{ ...source, site: newName }] : []) : [source]) : plugin.sources,
    })),
    views: library.views.map(filters),
    presetTags: kind === 'tags' ? {
      purchased: library.presetTags.purchased === oldName && newName ? newName : library.presetTags.purchased,
      archived: library.presetTags.archived === oldName && newName ? newName : library.presetTags.archived,
      wishlist: newName ? replace(library.presetTags.wishlist) : library.presetTags.wishlist,
    } : library.presetTags,
  }
}
