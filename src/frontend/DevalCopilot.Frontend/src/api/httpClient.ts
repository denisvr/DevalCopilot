import { getLaunchSession } from './session'

/**
 * The fetch-like shape every generated client accepts. Every request carries the
 * launch-session secret as a normal Authorization header — never a query string.
 */
export const authenticatedHttp = {
  fetch(url: RequestInfo, init?: RequestInit): Promise<Response> {
    const session = getLaunchSession()
    const headers = new Headers(init?.headers)

    if (session) {
      headers.set('Authorization', `Bearer ${session.secret}`)
    }

    return window.fetch(url, { ...init, headers })
  },
}

export function getApiBaseUrl(): string {
  return getLaunchSession()?.baseUrl ?? ''
}
