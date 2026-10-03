import { useCallback, useEffect, useRef, useState } from 'react'
import { useRunActionLifetime } from './useRunActionLifetime'

interface ActionState {
  runId: string
  busy: boolean
  error: string | null
  // The evidence refresh generation the message was reported under.
  epoch: number
}

export interface RunScopedActionOptions {
  /** Maps a failure to the fixed, safe message to show; raw exception text is never displayed. */
  toMessage: (caught: unknown) => string
  /** Invoked once, only when the request succeeded and its lifetime is still current. */
  onSuccess?: () => void
  /** Distinguishes independent controls sharing one hook; submissions of one control never overlap. */
  control?: string
}

export interface RunScopedAction {
  busy: boolean
  error: string | null
  /**
   * Runs one request bound to `requestRunId`. Resolves true only when the server accepted it AND
   * the interaction lifetime is still current; false for a refusal, a failure, a duplicate of an
   * in-flight submission, a run that is not the current one, or an obsolete completion. An
   * obsolete completion does not cancel or undo a request the server already accepted; the next
   * authoritative read shows its real outcome.
   */
  run: (requestRunId: string, execute: () => Promise<unknown>, options: RunScopedActionOptions) => Promise<boolean>
  /** Shows a local (for example validation) message for the current run, only from a handler of
   * the active lifetime, and without releasing a pending submission. */
  reportError: (message: string) => void
  /** Captures the current lifetime for component continuations. */
  capture: () => () => boolean
}

/**
 * The shared pending/error state of one asynchronous control, bound to the run and the mounted
 * interaction lifetime. Switching runs or unmounting ends the lifetime: the state is dropped, and
 * a completion that belongs to an ended lifetime is ignored after every asynchronous boundary.
 */
export function useRunScopedAction(runId: string, epoch = 0): RunScopedAction {
  const lifetime = useRunActionLifetime(runId)
  const [state, setState] = useState<ActionState | null>(null)
  // A message reported under an earlier epoch is obsolete (the host's evidence was re-read since): it is no longer shown, and a
  // pending submission is never touched. Read at the moment a message is recorded, so the memoized `run` stays stable.
  const epochRef = useRef(epoch)
  useEffect(() => {
    epochRef.current = epoch
  }, [epoch])

  useEffect(
    () => () => {
      setState(null)
    },
    [runId],
  )

  const run = useCallback(
    async (requestRunId: string, execute: () => Promise<unknown>, options: RunScopedActionOptions) => {
      const ticket = lifetime.begin(requestRunId, options.control)
      if (!ticket) {
        return false
      }
      setState({ runId: requestRunId, busy: true, error: null, epoch: epochRef.current })
      try {
        await execute()
      } catch (caught: unknown) {
        if (ticket.isCurrent()) {
          setState({ runId: requestRunId, busy: false, error: options.toMessage(caught), epoch: epochRef.current })
        }
        return false
      } finally {
        ticket.release()
      }
      if (!ticket.isCurrent()) {
        return false
      }
      setState({ runId: requestRunId, busy: false, error: null, epoch: epochRef.current })
      options.onSuccess?.()
      return true
    },
    [lifetime],
  )

  const reportError = useCallback(
    (message: string) => {
      if (!lifetime.isActive()) {
        return
      }
      // A local message never releases a submission that is still pending for this run.
      setState((current) => ({ runId, busy: current?.runId === runId ? current.busy : false, error: message, epoch: epochRef.current }))
    },
    [lifetime, runId],
  )

  const belongsToCurrentRun = state?.runId === runId
  return {
    busy: belongsToCurrentRun ? state.busy : false,
    error: belongsToCurrentRun && state.epoch === epoch ? state.error : null,
    run,
    reportError,
    capture: lifetime.capture,
  }
}
