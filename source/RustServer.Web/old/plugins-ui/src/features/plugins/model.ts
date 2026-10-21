import { HttpError } from '../../shared/api'
import type { IncomingChangeResponse } from '../../shared/api'

export type ResourceState<T> =
  | { status: 'loading' }
  | { status: 'available'; data: T }
  | { status: 'unavailable'; message: string }

export interface OperatorErrorPresentation {
  conflict: string
  forbidden: string
  notFound: string
  invalid: string
}

const defaultErrorPresentation: OperatorErrorPresentation = {
  conflict: 'Deployment or release creation is blocked',
  forbidden: 'This action is forbidden',
  notFound: 'The requested resource was not found',
  invalid: 'The request was invalid',
}

export function getOperatorErrorMessage(
  error: unknown,
  presentation: Partial<OperatorErrorPresentation> = {},
): string {
  const labels = { ...defaultErrorPresentation, ...presentation }
  if (error instanceof HttpError) {
    const detail = error.problemDetails?.detail?.trim()
    const title = error.problemDetails?.title?.trim()
    const backendMessage = detail || title

    if (error.status === 409) {
      return backendMessage
        ? `${labels.conflict}: ${backendMessage}`
        : `${labels.conflict} by the current backend state.`
    }

    if (error.status === 403) {
      return backendMessage
        ? `${labels.forbidden}: ${backendMessage}`
        : `${labels.forbidden} by the current backend environment or authorization.`
    }

    if (error.status === 404) {
        return backendMessage ? `${labels.notFound}: ${backendMessage}` : `${labels.notFound}.`
    }

    if (error.status === 400) {
        return backendMessage ? `${labels.invalid}: ${backendMessage}` : `${labels.invalid}.`
    }

    return backendMessage || error.message
  }

  return error instanceof Error && error.message ? error.message : 'Request failed.'
}

export function shortSha(sha: string): string {
  return sha.length > 12 ? `${sha.slice(0, 12)}…` : sha
}

export function formatTimestamp(timestamp: string): string {
  const date = new Date(timestamp)

  return Number.isNaN(date.valueOf()) ? timestamp : date.toLocaleString()
}

export function isOlderRelease(candidateId: string, currentId: string): boolean {
  const candidate = Number(candidateId)
  const current = Number(currentId)

  return (
    Number.isSafeInteger(candidate) &&
    Number.isSafeInteger(current) &&
    candidate < current
  )
}

/** Backend classifications and change types determine commit eligibility. */
export function isIncomingChangeCommitEligible(change: IncomingChangeResponse): boolean {
  return change.classification === 'managed' && change.changeType !== 'unchanged'
}
