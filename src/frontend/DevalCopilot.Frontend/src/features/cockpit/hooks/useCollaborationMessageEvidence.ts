import { useCallback, useLayoutEffect, useRef, useState } from 'react'
import type { CollaborationMessageEvidenceResponse } from '../../../api/clients'
import { collaborationMessageEvidenceClient } from '../../../api/clients'

export type CollaborationMessageEvidenceState =
  | { status: 'idle' }
  | { status: 'loading' }
  | { status: 'success'; evidence: CollaborationMessageEvidenceResponse }
  // The message legitimately has no linked Agent attempt (should not normally be reachable given
  // the card-level gating that only offers this control for a ProviderObserved card with a
  // non-null attemptId, but handled explicitly rather than assumed impossible).
  | { status: 'unavailable' }
  // The message's own attempt link could not be trusted (missing, non-Agent, or a role/provider
  // mismatch) — the backend's own fail-closed AttemptLinkBroken status. Distinct from
  // 'unavailable': this is never a benign "no evidence" case, and must never render the
  // historical-evidence success panel.
  | { status: 'broken' }
  | { status: 'error'; message: string }

export interface UseCollaborationMessageEvidenceResult {
  state: CollaborationMessageEvidenceState
  /** Fetches evidence using exactly this hook's own `runId`/`messageId` — never a "current" or
   * "latest" id inferred from elsewhere in state. Safe to call again from the `error` state to
   * retry, or from `idle` on first expand. A call while already loading is a no-op. */
  fetchEvidence: () => void
}

/**
 * Fetches bounded, historical Attempt evidence for one collaboration message, scoped to exactly
 * one `(runId, messageId)` pair. State is on-demand (idle until first requested), isolated per
 * hook instance, and resets to `idle` whenever `runId` or `messageId` changes.
 *
 * The visible reset happens during render, not in an effect — React's own "adjusting state while
 * rendering" pattern (comparing the new props against the previous render's own props, held in
 * `useState`, and calling `setState` synchronously in the render body when they differ). An
 * effect-based reset commits one extra render with the OLD (stale) evidence still visible under
 * the NEW ids before the effect runs and clears it; the render-time reset instead bails out and
 * re-renders before anything is ever painted, so a run/card switch can never flash or briefly
 * expose a previous run's or a previous card's evidence — independent of when any effect runs.
 *
 * The internal bookkeeping refs (`generationRef`/`loadingRef`) that let an in-flight fetch tell
 * whether its own response is still wanted are instead synchronized from a `useLayoutEffect`, not
 * from the render body itself: React refs are only ever read or written outside of rendering (in
 * effects or event handlers), never during it. A layout effect still runs synchronously before
 * the browser paints and before any new event (including a promise callback) can run, so this
 * carries no timing gap a stale response could exploit.
 *
 * Never persists fetched evidence to `localStorage`/`sessionStorage`/the URL.
 */
export function useCollaborationMessageEvidence(
  runId: string,
  messageId: string,
): UseCollaborationMessageEvidenceResult {
  const [state, setState] = useState<CollaborationMessageEvidenceState>({ status: 'idle' })
  // Identifies the current (runId, messageId) generation so a stale in-flight fetch from a prior
  // pair can never overwrite state for the pair that is current by the time it resolves. Only
  // ever read/written from `fetchEvidence`'s async continuations and the layout effect below —
  // never from the render body itself.
  const generationRef = useRef(0)
  // Mirrors `state.status === 'loading'` without a stale-closure risk, so `fetchEvidence` keeps
  // one stable identity per (runId, messageId) pair while still reliably no-op-ing a re-entrant
  // call made while a fetch is already in flight.
  const loadingRef = useRef(false)
  // The (runId, messageId) pair as of the most recently rendered generation — compared against the
  // CURRENT props on every render, synchronously, so a change is caught before this render commits
  // rather than one render later inside an effect. Plain component state, never a ref, because
  // this comparison happens in the render body itself.
  const [renderedPair, setRenderedPair] = useState({ runId, messageId })

  if (renderedPair.runId !== runId || renderedPair.messageId !== messageId) {
    setRenderedPair({ runId, messageId })
    setState({ status: 'idle' })
  }

  useLayoutEffect(() => {
    generationRef.current += 1
    loadingRef.current = false
  }, [runId, messageId])

  const fetchEvidence = useCallback(() => {
    if (loadingRef.current) {
      return
    }

    const generation = generationRef.current
    loadingRef.current = true
    setState({ status: 'loading' })

    collaborationMessageEvidenceClient()
      .getCollaborationMessageEvidence(runId, messageId)
      .then((evidence) => {
        if (generationRef.current !== generation) {
          return
        }

        loadingRef.current = false
        switch (evidence.evidenceStatus) {
          case 'HasEvidence':
            setState({ status: 'success', evidence })
            break
          case 'NoAgentEvidence':
            setState({ status: 'unavailable' })
            break
          case 'AttemptLinkBroken':
            setState({ status: 'broken' })
            break
          default:
            // An evidenceStatus this client does not recognize is never rendered as if it were a
            // known, safe state (and never as the success panel) — reject it explicitly.
            setState({ status: 'error', message: 'Attempt evidence is unavailable.' })
        }
      })
      .catch(() => {
        if (generationRef.current === generation) {
          loadingRef.current = false
          setState({ status: 'error', message: 'Attempt evidence is unavailable.' })
        }
      })
  }, [runId, messageId])

  return { state, fetchEvidence }
}
