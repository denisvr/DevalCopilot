/** A command's request is aborted after this long; it is never retried. */
export const BOUNDED_COMMAND_TIMEOUT_MS = 10_000

export interface BoundedCommand {
  /** The one request's own settlement: its answer, or its failure (an abort included). It settles only when the transport has. */
  result: Promise<unknown>
  /** Aborts the actual request and ends the bound at once. */
  cancel: () => void
}

/** Sends one request with a real ten-second AbortController bound. The bound ends when the request settles. */
export function startBoundedCommand(request: (signal: AbortSignal) => Promise<unknown>, milliseconds = BOUNDED_COMMAND_TIMEOUT_MS): BoundedCommand {
  const controller = new AbortController()
  const timer = window.setTimeout(() => controller.abort(), milliseconds)
  let pending: Promise<unknown>
  try {
    pending = request(controller.signal)
  } catch (caught) {
    pending = Promise.reject(caught)
  }

  const result = pending.finally(() => window.clearTimeout(timer))
  return {
    result,
    cancel: () => {
      window.clearTimeout(timer)
      controller.abort()
    },
  }
}
