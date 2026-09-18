import { useCallback, useEffect, useRef, useState } from 'react'
import type { ClaudeCriticalReviewAttemptStatusResponse } from '../../../api/clients'
import { claudeCriticalReviewAttemptStatusClient } from '../../../api/clients'

export interface UseClaudeCriticalReviewAttemptStatusResult {
  status: ClaudeCriticalReviewAttemptStatusResponse | null
  loading: boolean
  error: string | null
  refresh: () => void
}

/** The result actually committed so far, tagged with the runId it belongs to. Kept as one
 * atomic value (never split into separate `status`/`error` state slots) so a single `setState`
 * call can never leave one field updated for the new run while another still reflects the old
 * one. */
interface CommittedResult {
  runId: string | null
  status: ClaudeCriticalReviewAttemptStatusResponse | null
  error: string | null
}

const EMPTY_RESULT: CommittedResult = { runId: null, status: null, error: null }

/**
 * Reads the most recent Claude critical-review attempt for a run. `latestEventSequence`
 * re-triggers a fetch whenever any run event advances (mirroring `useCollaborationTimeline`),
 * and `refresh` lets a caller force one immediately after successfully requesting a new
 * attempt, since claiming an attempt does not itself emit a run event.
 *
 * Isolation across a run switch is enforced at render time, not by an effect: `useEffect`
 * cannot run synchronously with the render that receives a new `runId`, so clearing state
 * inside an effect (a `setStatus(null)` at effect-start, however early) still lets at least
 * one commit through — the one produced by the render that triggered the effect in the first
 * place — showing the previous run's status/error briefly "on" the new run before that effect
 * ever executes. Instead, `committed` always remembers which `runId` it was actually fetched
 * for, and every render below computes `belongsToCurrentRun` fresh from props and state alone;
 * a mismatch masks `status`/`error` to null and forces `loading` true, on every single render,
 * with no dependency on when the effect happens to run.
 */
export function useClaudeCriticalReviewAttemptStatus(
  runId: string | null,
  latestEventSequence: number | undefined,
): UseClaudeCriticalReviewAttemptStatusResult {
  const [committed, setCommitted] = useState<CommittedResult>(EMPTY_RESULT)
  const [loading, setLoading] = useState(true)
  const [refreshToken, setRefreshToken] = useState(0)
  // Bumped on every runId change (including a plain unmount, in this effect's own cleanup) so
  // a late-arriving response for a run this hook has since navigated away from can never
  // update state for a different run — mirrors the generation-counter pattern in
  // `useRunCockpit`. Independent of, and in addition to, the render-time mask below: this
  // guards a response that arrives AFTER the effect for a newer runId has already started,
  // not the render-timing gap that mask closes.
  const generationRef = useRef(0)

  const refresh = useCallback(() => {
    setRefreshToken((token) => token + 1)
  }, [])

  useEffect(() => {
    const generation = ++generationRef.current

    if (!runId) {
      setCommitted(EMPTY_RESULT)
      setLoading(false)
      return
    }

    setLoading(true)
    claudeCriticalReviewAttemptStatusClient()
      .getClaudeCriticalReviewAttemptStatus(runId)
      .then((response) => {
        if (generationRef.current === generation) {
          // The endpoint always returns a real, well-formed body now: `hasAttempt` is the
          // explicit discriminator for "this run has never requested a Claude critical
          // review", never an ambiguous absent field to infer from.
          setCommitted({ runId, status: response.hasAttempt ? response : null, error: null })
        }
      })
      .catch(() => {
        // Covers both a genuine network/server failure and the endpoint's own 404 for an
        // unknown run. This hook is only ever driven by a run the cockpit already believes
        // exists, so a 404 here is an edge case (e.g. a race with the run being deleted), not
        // one that needs its own UI — folding it into the same generic message is enough, and
        // never leaks the underlying response detail.
        if (generationRef.current === generation) {
          setCommitted({ runId, status: null, error: 'Claude critical review attempt status is unavailable.' })
        }
      })
      .finally(() => {
        if (generationRef.current === generation) {
          setLoading(false)
        }
      })

    return () => {
      generationRef.current += 1
    }
  }, [runId, latestEventSequence, refreshToken])

  // Synchronous, render-time isolation: `committed` might still belong to a previous runId —
  // the effect above has not necessarily run yet for the current one. A mismatch masks
  // status/error and reports loading truthfully for the run actually being rendered, on every
  // render, never just eventually once the effect catches up.
  const belongsToCurrentRun = committed.runId === runId
  return {
    status: belongsToCurrentRun ? committed.status : null,
    error: belongsToCurrentRun ? committed.error : null,
    loading: belongsToCurrentRun ? loading : runId !== null,
    refresh,
  }
}
