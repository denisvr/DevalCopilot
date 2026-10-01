/** The only refusal worth retrying: the host's Git capability probe has not completed yet. */
export const GIT_UNAVAILABLE_CODE = 'projects.git_unavailable'

/** The structural part of a generated-client ApiException that the retry decision reads. */
export interface RefusalLike {
  status?: unknown
  response?: unknown
}

/** True only for HTTP 409 whose first problem error is exactly the Git readiness refusal. */
export function isGitUnavailableRefusal(error: unknown): boolean {
  const refusal = error as RefusalLike | null
  if (!refusal || refusal.status !== 409 || typeof refusal.response !== 'string') {
    return false
  }
  try {
    const parsed = JSON.parse(refusal.response) as { errors?: { code?: unknown }[] }
    return parsed.errors?.[0]?.code === GIT_UNAVAILABLE_CODE
  } catch {
    return false
  }
}

export interface ReadinessRetryOptions {
  /** Total attempts, first included; finite so a permanently unavailable host fails the test instead of hanging it. */
  attempts: number
  delayMs: number
  sleep?: (ms: number) => Promise<void>
}

/**
 * Runs one idempotent request (the caller builds every fixture BEFORE calling this, never inside the request), retrying only
 * the Git readiness refusal. Any other failure is surfaced at once as a fixed message with the status only: a transport error
 * can carry the full request, including the Authorization header, so neither it nor the response body is ever included.
 */
export async function retryOnGitUnavailable<T>(request: () => Promise<T>, options: ReadinessRetryOptions): Promise<T> {
  const sleep = options.sleep ?? ((ms: number) => new Promise<void>((resolve) => setTimeout(resolve, ms)))
  for (let attempt = 1; ; attempt += 1) {
    try {
      return await request()
    } catch (error) {
      if (!isGitUnavailableRefusal(error)) {
        const status = (error as RefusalLike | null)?.status
        throw new Error(
          `The request failed with an unexpected error${typeof status === 'number' ? ` (status ${status})` : ''}.`,
        )
      }
      if (attempt >= options.attempts) {
        throw new Error(`Git stayed unavailable after ${options.attempts} attempts.`)
      }
      await sleep(options.delayMs)
    }
  }
}
