import { useState } from 'react'
import { createTag, updateCollection } from './library'
import type { Collection } from './library'
import type { PrototypeLibrary } from './model'
import { Modal } from './Modal'
import styles from './prototype.module.css'

type Props = { library: PrototypeLibrary; onChange: (library: PrototypeLibrary) => void; onClose: () => void }

function ManagedCollection({ kind, library, onChange }: { kind: Collection; library: PrototypeLibrary; onChange: Props['onChange'] }) {
  const [name, setName] = useState('')
  const [editing, setEditing] = useState<string | null>(null)
  const [draft, setDraft] = useState('')
  const [error, setError] = useState('')
  const names = kind === 'tags' ? library.tags.map(tag => tag.name) : library[kind]
  const valid = (value: string, except?: string) => value.trim() && !names.some(item => item !== except && item.toLowerCase() === value.trim().toLowerCase())
  const add = () => {
    if (!valid(name)) { setError('Enter a unique, non-empty name.'); return }
    const value = name.trim()
    onChange(kind === 'tags' ? { ...library, tags: [...library.tags, createTag(value, library.tags)] } : { ...library, [kind]: [...library[kind], value] })
    setName(''); setError('')
  }
  const rename = (oldName: string) => {
    if (!valid(draft, oldName)) { setError('Enter a unique, non-empty name.'); return }
    onChange(updateCollection(library, kind, oldName, draft.trim()))
    setEditing(null); setError('')
  }
  return <section className={styles.manageSection}><h3>{kind === 'sites' ? 'Sources' : kind}</h3>
    <form className={styles.inlineForm} onSubmit={event => { event.preventDefault(); add() }}><label className={styles.srOnly} htmlFor={`new-${kind}`}>New {kind} name</label><input id={`new-${kind}`} value={name} onChange={event => setName(event.target.value)} placeholder={`New ${kind === 'sites' ? 'source' : kind.slice(0, -1)}`} /><button>Add</button></form>
    {error && <p role="alert" className={styles.error}>{error}</p>}
    <ul className={styles.manageList}>{names.map(item => <li key={item}>{editing === item ? <form className={styles.inlineForm} onSubmit={event => { event.preventDefault(); rename(item) }}><label className={styles.srOnly} htmlFor={`rename-${kind}`}>Rename {item}</label><input id={`rename-${kind}`} value={draft} onChange={event => setDraft(event.target.value)} autoFocus /><button>Save</button><button type="button" onClick={() => { setEditing(null); setError('') }}>Cancel</button></form> : <><span>{item}</span><div><button type="button" onClick={() => { setEditing(item); setDraft(item); setError('') }}>Rename<span className={styles.srOnly}> {item}</span></button><button type="button" onClick={() => { if (window.confirm(`Delete ${item}? ${kind === 'sites' ? 'Its associated source links' : 'Its assignments'} will be removed from plugins and saved views will be updated.`)) onChange(updateCollection(library, kind, item, null)) }}>Delete<span className={styles.srOnly}> {item}</span></button></div></>}</li>)}</ul>
  </section>
}

export function LibraryManager({ library, onChange, onClose }: Props) {
  return <Modal titleId="manage-title" onClose={onClose}><header className={styles.dialogHeader}><div><h2 id="manage-title">Manage library</h2><p>Names and assignments are stored in this browser.</p></div><button type="button" onClick={onClose}>Close</button></header><div className={styles.managerGrid}>{(['folders', 'tags', 'sites'] as const).map(kind => <ManagedCollection key={kind} kind={kind} library={library} onChange={onChange} />)}</div></Modal>
}
