import type { BaselineResponse, TargetSourceRequest } from '../../../shared/api'

/** A UI-only source choice that has not been applied to the server. */
export type SourceDraft =
  | { kind: 'development' }
  | { kind: 'release'; version: string }

export function toTargetSourceRequest(draft: SourceDraft): TargetSourceRequest {
  return draft.kind === 'development'
    ? { source: 'development' }
    : { source: 'release', release: draft.version }
}

export function formatSource(type: string, release: string | null): string {
  if (type === 'development') {
    return 'Development · develop'
  }

  return type === 'release' && release !== null
    ? `Release · release/${release}`
    : type
}

/** Uses only the persisted applied baseline, never a local Git branch. */
export function draftFromBaseline(baseline: BaselineResponse | null): SourceDraft | null {
  if (baseline === null) {
    return null
  }

  if (baseline.source === 'development') {
    return { kind: 'development' }
  }

  return baseline.source === 'release' && baseline.release !== null
    ? { kind: 'release', version: baseline.release }
    : null
}

export function formatDraft(draft: SourceDraft): string {
  return draft.kind === 'development'
    ? 'Development · develop'
    : `Release · release/${draft.version}`
}
