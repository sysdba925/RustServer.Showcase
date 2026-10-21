import { useLayoutEffect, type PropsWithChildren } from 'react'

import type { PluginsApiResponder } from './plugins-api-responder'

interface PluginsApiMockProps extends PropsWithChildren {
  responder: PluginsApiResponder
}

function toUrl(input: RequestInfo | URL): URL {
  if (input instanceof Request) {
    return new URL(input.url)
  }

  return new URL(input.toString(), window.location.origin)
}

function isPluginsApiRequest(url: URL): boolean {
  return url.pathname === '/plugins-api' || url.pathname.startsWith('/plugins-api/')
}

/**
 * A Storybook-only HTTP boundary for API-bound Plugins pages. It is installed
 * before child effects run and restored when the story unmounts.
 */
export function PluginsApiMock({ children, responder }: PluginsApiMockProps) {
  useLayoutEffect(() => {
    const originalFetch = globalThis.fetch

    globalThis.fetch = async (input, init) => {
      const url = toUrl(input)

      if (!isPluginsApiRequest(url)) {
        return originalFetch(input, init)
      }

      const requestMethod = init?.method ?? (input instanceof Request ? input.method : 'GET')
      const path = url.pathname.replace(/^\/plugins-api(?=\/|$)/, '')
      const response = responder({
        method: requestMethod.toUpperCase(),
        path,
        url,
      })

      return new Response(JSON.stringify(response.body), {
        status: response.status ?? 200,
        headers: {
          'Content-Type': 'application/json',
        },
      })
    }

    return () => {
      globalThis.fetch = originalFetch
    }
  }, [responder])

  return children
}
