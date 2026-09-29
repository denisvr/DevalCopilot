import { useCallback, useLayoutEffect, useRef, useState } from 'react'
import type { SealedAgentArtifactWindowResponse } from '../../../api/clients'

export const SEALED_WINDOW_MAX_BYTES = 16 * 1024

export type SealedAgentArtifactWindowState =
  | { status: 'idle' }
  | { status: 'loading' }
  | {
      status: 'loaded'
      text: string
      nextOffset: number
      totalLengthSoFar: number
      isComplete: boolean
      truncated: boolean | null
    }
  // NoAgentEvidence / AttemptLinkBroken / PurposeNotAllowlisted / ArtifactNotFound — no sealed
  // artifact evidence exists for this purpose, and never presented as an error.
  | { status: 'unavailable' }
  // The recorded artifact's sealed file could not be found on disk (including a rejected,
  // path-escaping stored path — the backend never distinguishes the two, both fail the same way).
  | { status: 'missing' }
  | { status: 'integrity-mismatch' }
  // An attempt selected from the run history whose persisted identity could not be proven
  // coherent: its artifacts are never served.
  | { status: 'identity-invalid' }
  | { status: 'error'; message: string }

export interface UseSealedAgentArtifactWindowResult {
  state: SealedAgentArtifactWindowState
  /** Fetches the first window when idle/errored, or the next window once a prior one has
   * loaded — never a "current" or "latest" offset inferred from elsewhere in state. A call while
   * already loading, or before a purpose is selected, is a no-op. */
  loadNextWindow: () => void
}

export type SealedWindowRequest = (
  purpose: string,
  fromOffset: number,
  maxBytes: number,
) => Promise<SealedAgentArtifactWindowResponse>

/**
 * Fetches bounded, verified text windows of one sealed Agent-attempt artifact, scoped to exactly
 * one `(scopeKey, purpose)` pair. `scopeKey` identifies the run and the message or attempt the
 * caller resolved the artifact through, and `request` performs that route's fetch; the accumulation,
 * reset, and retry behavior is identical for every route. State is on-demand (idle until first
 * requested) and resets to `idle` whenever the scope or purpose changes — mirrors
 * `useCollaborationMessageEvidence`'s own render-time reset pattern rather than an effect-based
 * reset, so a change can never flash a previous purpose's or a previous scope's text before this
 * hook's own state catches up.
 *
 * Fetched text is accumulated across windows entirely in a ref, not component state, and is
 * reset alongside `state` on every scope or purpose change — accumulation must never survive a
 * purpose, attempt, or run switch. Never persists fetched text to `localStorage`/`sessionStorage`/
 * the URL, and never logs it.
 *
 * A failed request for a LATER window (one requested after at least one window already loaded)
 * can be retried without duplicating or losing text: the failed offset is tracked independently
 * of `state`, a failure never touches the accumulated text, and retrying re-requests that same
 * offset rather than restarting from 0.
 */
export function useSealedArtifactWindow(
  scopeKey: string,
  purpose: string | null,
  request: SealedWindowRequest,
): UseSealedAgentArtifactWindowResult {
  const [state, setState] = useState<SealedAgentArtifactWindowState>({ status: 'idle' })
  const generationRef = useRef(0)
  const loadingRef = useRef(false)
  const accumulatedTextRef = useRef('')
  // The offset most recently requested (regardless of outcome) — lets a retry after a failed
  // LATER window re-request that same failed offset rather than assuming offset 0.
  const lastRequestedOffsetRef = useRef(0)
  const [renderedScope, setRenderedScope] = useState({ scopeKey, purpose })

  if (renderedScope.scopeKey !== scopeKey || renderedScope.purpose !== purpose) {
    setRenderedScope({ scopeKey, purpose })
    setState({ status: 'idle' })
  }

  // accumulatedTextRef is never read during render (only inside fetchWindow's async
  // continuations), so clearing it here — alongside the other bookkeeping refs — is safe and keeps
  // the reset co-located with the generation bump it must happen before any new fetch can start.
  useLayoutEffect(() => {
    generationRef.current += 1
    loadingRef.current = false
    accumulatedTextRef.current = ''
    lastRequestedOffsetRef.current = 0
  }, [scopeKey, purpose])

  const fetchWindow = useCallback(
    (fromOffset: number) => {
      if (loadingRef.current || purpose === null) {
        return
      }

      const generation = generationRef.current
      loadingRef.current = true
      lastRequestedOffsetRef.current = fromOffset
      setState({ status: 'loading' })

      request(purpose, fromOffset, SEALED_WINDOW_MAX_BYTES)
        .then((response: SealedAgentArtifactWindowResponse) => {
          if (generationRef.current !== generation) {
            return
          }

          loadingRef.current = false
          switch (response.status) {
            case 'Ok': {
              accumulatedTextRef.current += response.text ?? ''
              const nextOffset = response.nextOffset ?? fromOffset
              const totalLengthSoFar = response.totalLengthSoFar ?? 0
              setState({
                status: 'loaded',
                text: accumulatedTextRef.current,
                nextOffset,
                totalLengthSoFar,
                isComplete: nextOffset >= totalLengthSoFar,
                truncated: response.truncated ?? null,
              })
              break
            }
            case 'Missing':
              setState({ status: 'missing' })
              break
            case 'IntegrityMismatch':
              setState({ status: 'integrity-mismatch' })
              break
            case 'AttemptIdentityInvalid':
              setState({ status: 'identity-invalid' })
              break
            case 'NoAgentEvidence':
            case 'AttemptLinkBroken':
            case 'PurposeNotAllowlisted':
            case 'ArtifactNotFound':
              setState({ status: 'unavailable' })
              break
            default:
              // A status this client does not recognize is never rendered as if it were a known,
              // safe state — reject it explicitly, exactly like the collaboration-evidence hook.
              setState({ status: 'error', message: 'This artifact window is unavailable.' })
          }
        })
        .catch(() => {
          if (generationRef.current === generation) {
            loadingRef.current = false
            setState({ status: 'error', message: 'This artifact window is unavailable.' })
          }
        })
    },
    [purpose, request],
  )

  const loadNextWindow = useCallback(() => {
    if (state.status === 'loaded') {
      fetchWindow(state.nextOffset)
      return
    }

    if (state.status === 'error') {
      // Retries the exact offset that failed, preserving any earlier successfully-accumulated
      // text (never touched on failure) — never restarts at offset 0, which would re-fetch and
      // re-append already-accumulated text and duplicate it.
      fetchWindow(lastRequestedOffsetRef.current)
      return
    }

    fetchWindow(0)
  }, [state, fetchWindow])

  return { state, loadNextWindow }
}
