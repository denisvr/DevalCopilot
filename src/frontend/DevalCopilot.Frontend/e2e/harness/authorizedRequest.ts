// Authorized reads for the end-to-end checks. A Playwright transport error carries the complete request, including the
// Authorization header, in its message and call log, so no raw exception, cause, stack, or response payload from the
// request ever leaves this module: callers only see a fixed message with a label and, for a refusal, the status code.

export interface AuthorizedResponse {
  ok(): boolean
  status(): number
  json(): Promise<unknown>
}

export interface AuthorizedRequestContext {
  get(url: string, options: { headers: Record<string, string> }): Promise<AuthorizedResponse>
}

export async function getAuthorizedJson<T>(
  request: AuthorizedRequestContext,
  url: string,
  token: string,
  label: string,
): Promise<T> {
  let response: AuthorizedResponse
  try {
    response = await request.get(url, { headers: { Authorization: `Bearer ${token}` } })
  } catch {
    throw new Error(`${label} request failed before a response was received.`)
  }
  if (!response.ok()) {
    throw new Error(`${label} request was refused with status ${response.status()}.`)
  }
  try {
    return (await response.json()) as T
  } catch {
    throw new Error(`${label} response could not be read.`)
  }
}
