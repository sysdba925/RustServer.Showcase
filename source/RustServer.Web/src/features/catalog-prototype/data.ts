import type { LibraryPlugin, LibraryTag } from './model'

const subjects = ['Market', 'Raid', 'Map', 'Craft', 'Weather', 'Vehicle', 'Quest', 'Base', 'Animal', 'Event', 'Chat', 'Turret', 'Harvest', 'Fishing', 'Spawn', 'Trade', 'Clan', 'Door', 'Loot', 'Workbench']
const features = ['Assistant', 'Manager', 'Toolkit', 'Journal', 'Signals']
const sites = ['uMod', 'Codefling', 'Lone.Design', 'MyVector', 'Chaos Addons']
const authors = ['Northstar', 'Copper Fox', 'Moss', 'Atlas', 'Field Notes', 'Harbor', 'Raven', 'Oak', 'Waypoint', 'Ember']
const tags: LibraryTag[] = [
  { name: 'Nice to have', color: '#7f9abd' },
  { name: 'Planned purchase', color: '#c49a60' },
  { name: 'Purchased', color: '#8da96b' },
  { name: 'Archived', color: '#b98280' },
]
const folders = ['Shop', 'Admin', 'Experiments', 'Archive']

// Stable, fictional records exercise optional fields and combinations from the concept.
const plugins: LibraryPlugin[] = subjects.flatMap((subject, subjectIndex) =>
  features.map((feature, featureIndex) => {
    const i = subjectIndex * features.length + featureIndex
    const site = sites[i % sites.length]
    return {
      id: `fixture-${i + 1}`,
      slug: `${subject}-${feature}`.toLowerCase(),
      name: `${subject} ${feature}`,
      ...(i % 9 !== 0 && { author: authors[(i + subjectIndex) % authors.length] }),
      ...(i % 7 !== 0 && { description: `Tools for ${subject.toLowerCase()} workflows, with configurable options for your community.` }),
      ...(i % 5 === 0 && { sourceDescription: `## Overview\n\nA fictional ${subject.toLowerCase()} plugin for your server.\n\n- Configurable options\n- Simple setup\n\nSee [documentation](https://example.com/docs).\n\n\`\`\`csharp\n// Example configuration\n\`\`\`` }),
      ...(i % 3 === 0 && { lastUpdate: `2025-${String(i % 12 + 1).padStart(2, '0')}-15` }),
      ...(i % 11 === 0 && { notes: 'Review this idea before adding it to the library.' }),
      sources: i % 13 === 0 ? [] : [
        { site, url: `https://example.com/${site.toLowerCase().replace(/[^a-z]/g, '')}/${i + 1}` },
        ...(i % 6 === 0 ? [{ site: 'uMod', url: `https://example.com/umod/${i + 1}` }] : []),
      ],
      ...(i % 4 !== 0 && { price: { amount: [4.99, 9.5, 14.99, 24, 0][i % 5], currency: i % 3 === 0 ? 'EUR' : 'USD' } }),
      tags: i % 8 === 0 ? [] : [tags[i % tags.length], ...(i % 10 === 2 ? [tags[3]] : [])],
      folders: i % 7 === 0 ? [] : [folders[i % folders.length], ...(i % 9 === 1 && i % folders.length !== 2 ? ['Experiments'] : [])],
    }
  }),
)

export function getPrototypePlugins(): readonly LibraryPlugin[] { return plugins }
