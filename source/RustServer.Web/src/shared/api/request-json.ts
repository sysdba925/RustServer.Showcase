import type { ProblemDetails } from './control-types'

type HttpRequestMethod = 'GET' | 'POST' | 'PUT' | 'DELETE'

let authorizationRedirectStarted = false

/** Applies the shared authorization response handling to API requests. */
export async function fetchApi(url: string, request: RequestInit): Promise<Response> {
  const response = await fetch(url, request)

  if (response.status === 401 && !authorizationRedirectStarted) {
    authorizationRedirectStarted = true
    window.location.replace('/')
  }

  return response
}

interface HttpErrorOptions {
  method: HttpRequestMethod
  url: string
  status: number
  statusText: string
  problemDetails?: ProblemDetails
  responseBody?: unknown
}

/** An unsuccessful HTTP response returned by the shared API boundary. */
export class HttpError extends Error {
  readonly method: HttpRequestMethod
  readonly url: string
  readonly status: number
  readonly statusText: string
  readonly problemDetails: ProblemDetails | undefined
  /** Parsed JSON error payload when the server supplied one. */
  readonly responseBody: unknown | undefined

  constructor({
    method,
    url,
    status,
    statusText,
    problemDetails,
    responseBody,
  }: HttpErrorOptions) {
    super(
      `${method} ${url} failed with HTTP ${status} (${statusText || 'Unknown status'})`,
    )
    this.name = 'HttpError'
    this.method = method
    this.url = url
    this.status = status
    this.statusText = statusText
    this.problemDetails = problemDetails
    this.responseBody = responseBody
  }
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

function isNullableString(value: unknown): boolean {
  return value === null || typeof value === 'string'
}

function isNullableInteger(value: unknown): boolean {
  return value === null || (typeof value === 'number' && Number.isInteger(value))
}

function isProblemDetails(value: unknown): value is ProblemDetails {
  if (!isRecord(value)) {
    return false
  }

  const problemKeys = ['type', 'title', 'status', 'detail', 'instance']
  if (!problemKeys.some((key) => key in value)) {
    return false
  }

  return (
    (!('type' in value) || isNullableString(value.type)) &&
    (!('title' in value) || isNullableString(value.title)) &&
    (!('status' in value) || isNullableInteger(value.status)) &&
    (!('detail' in value) || isNullableString(value.detail)) &&
    (!('instance' in value) || isNullableString(value.instance))
  )
}

function isJsonResponse(response: Response): boolean {
  const contentType = response.headers?.get('content-type')?.split(';', 1)[0]
    .trim()
    .toLowerCase()

  return contentType === 'application/json' ||
    contentType === 'text/json' ||
    contentType?.endsWith('+json') === true
}

async function readErrorBody(response: Response): Promise<unknown | undefined> {
  if (!isJsonResponse(response)) {
    return undefined
  }

  try {
    return await response.json() as unknown
  } catch {
    return undefined
  }
}

/** Performs a JSON request and preserves contract-shaped failure details. */
export async function requestJson<T>(
  method: HttpRequestMethod,
  url: string,
  signal?: AbortSignal,
  body?: unknown,
): Promise<T> {
  const request: RequestInit = {
    method,
    signal,
  }

  if (body !== undefined) {
    if (body instanceof FormData) {
      request.body = body
    } else {
      request.headers = { 'Content-Type': 'application/json' }
      request.body = JSON.stringify(body)
    }
  }

  const response = await fetchApi(url, request)

  if (!response.ok) {
    const responseBody = await readErrorBody(response)

    throw new HttpError({
      method,
      url,
      status: response.status,
      statusText: response.statusText || '',
      problemDetails: isProblemDetails(responseBody) ? responseBody : undefined,
      responseBody,
    })
  }

  return (await response.json()) as T
}

/** Performs a successful request whose contract response has no body. */
export async function requestNoContent(
  method: HttpRequestMethod,
  url: string,
  signal?: AbortSignal,
): Promise<void> {
  const response = await fetchApi(url, { method, signal })

  if (!response.ok) {
    const responseBody = await readErrorBody(response)

    throw new HttpError({
      method,
      url,
      status: response.status,
      statusText: response.statusText || '',
      problemDetails: isProblemDetails(responseBody) ? responseBody : undefined,
      responseBody,
    })
  }
}
