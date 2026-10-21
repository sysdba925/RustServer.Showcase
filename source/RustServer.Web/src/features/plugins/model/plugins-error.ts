import { HttpError } from '../../../shared/api'

interface PluginsApiError {
  code: string
  message: string
  details: Record<string, unknown | null> | null
}

export function asPluginsApiError(value: unknown): PluginsApiError | null {
  if (typeof value !== 'object' || value === null || Array.isArray(value)) {
    return null
  }

  const candidate = value as Record<string, unknown>
  if (typeof candidate.code !== 'string' || typeof candidate.message !== 'string') {
    return null
  }

  const details = candidate.details
  if (details !== null && details !== undefined &&
    (typeof details !== 'object' || Array.isArray(details))) {
    return null
  }

  return {
    code: candidate.code,
    message: candidate.message,
    details: details === undefined ? null : details as Record<string, unknown | null> | null,
  }
}

export function getPluginsErrorMessage(error: unknown): string {
  if (error instanceof HttpError) {
    const apiError = asPluginsApiError(error.responseBody)
    if (apiError !== null) {
      return `${apiError.code}: ${apiError.message}`
    }

    const detail = error.problemDetails?.detail?.trim()
    if (detail) {
      return detail
    }
  }

  return error instanceof Error && error.message !== ''
    ? error.message
    : 'Request failed.'
}

export function getPluginsOperationIdFromError(error: unknown): string | null {
  if (!(error instanceof HttpError)) {
    return null
  }

  const operationId = asPluginsApiError(error.responseBody)?.details?.operationId
  return typeof operationId === 'string' && operationId !== '' ? operationId : null
}
