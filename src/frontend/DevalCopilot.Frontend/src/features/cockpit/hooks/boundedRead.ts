/** Each read's request is aborted after this long: a hung request can never keep an earlier success current. */
export const BOUNDED_READ_TIMEOUT_MS = 10_000

export type BoundedReadFailure = 'timeout' | 'read' | 'not-found'
export type BoundedReadAnswer = { raw: unknown } | { failure: BoundedReadFailure }

export interface BoundedRead {
  /** Settles what is shown: the parsed answer or the first failure, whichever happens first; the loser is ignored. */
  answer: Promise<BoundedReadAnswer>
  /** Settles only once the actual request has (answered, failed or aborted), so a flight never overlaps the next one. */
  transport: Promise<void>
  /** Aborts the actual request and ends the bound at once, so no timer is left running. */
  cancel: () => void
}

const isNotFound = (caught: unknown): boolean =>
  typeof caught === 'object' && caught !== null && (caught as { status?: unknown }).status === 404

/**
 * One GET with a real ten-second bound: when the bound elapses the request is aborted, not merely abandoned. `request` receives the
 * signal that cancels its actual transport.
 */
export function startBoundedRead(request: (signal: AbortSignal) => Promise<unknown>, milliseconds = BOUNDED_READ_TIMEOUT_MS): BoundedRead {
  const controller = new AbortController()
  let timer: number | undefined
  let transport: Promise<void> = Promise.resolve()
  const answer = new Promise<BoundedReadAnswer>((resolve) => {
    let settled = false
    const finish = (value: BoundedReadAnswer) => {
      if (!settled) {
        settled = true
        window.clearTimeout(timer)
        resolve(value)
      }
    }

    timer = window.setTimeout(() => {
      finish({ failure: 'timeout' })
      controller.abort()
    }, milliseconds)

    let pending: Promise<unknown>
    try {
      pending = request(controller.signal)
    } catch (caught) {
      pending = Promise.reject(caught)
    }

    transport = pending.then(
      (raw) => finish({ raw }),
      (caught: unknown) => finish({ failure: isNotFound(caught) ? 'not-found' : 'read' }),
    )
  })

  return {
    answer,
    transport,
    cancel: () => {
      window.clearTimeout(timer)
      controller.abort()
    },
  }
}
