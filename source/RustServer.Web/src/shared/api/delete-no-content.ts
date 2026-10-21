import { requestNoContent } from './request-json'

/** Performs a DELETE request and expects an empty successful response. */
export function deleteNoContent(url: string, signal?: AbortSignal): Promise<void> {
  return requestNoContent('DELETE', url, signal)
}
