import { useCallback, useLayoutEffect, useRef, useState } from 'react'
import type { SealedAgentArtifactWindowResponse } from '../../../api/clients'
import { sealedAgentArtifactWindowClient } from '../../../api/clients'

const DEFAULT_MAX_BYTES = 16 * 1024

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
  | { status: 'error'; message: string }

export interface UseSealedAgentArtifactWindowResult {
  state: SealedAgentArtifactWindowState
  /** Fetches the first window when idle/errored, or the next window once a prior one has
   * loaded — never a "current" or "latest" offset inferred from elsewhere in state. A call while
   * already loading, or before a purpose is selected, is a no-op. */
  loadNextWindow: () => void
}

/**
 * Fetches bounded, verified text windows of one sealed Agent-attempt artifact, scoped to exactly
 * one `(runId, messageId, purpose)` triple. State is on-demand (idle until first requested) and
 * resets to `idle` whenever any of the three changes — mirrors
 * `useCollaborationMessageEvidence`'s own render-time reset pattern (see that hook's own doc
 * comment for the full rationale) rather than an effect-based reset, so a triple change can never
 * flash a previous purpose's or a previous message's text before this hook's own state catches up.
 *
 * Fetched text is accumulated across windows entirely in a ref, not component state, and is
 * reset alongside `state` on every triple change — accumulation must never survive a purpose or
 * message switch. Never persists fetched text to `localStorage`/`sessionStorage`/the URL.
 *
 * A failed request for a LATER window (one requested after at least one window already loaded)
 * can be retried without duplicating or losing text: the failed offset is tracked independently
 * of `state`, a failure never touches the accumulated text, and retrying re-requests that same
 * offset rather than restarting from 0 — which would otherwise re-fetch and re-append the
 * already-accumulated earlier text on top of itself.
 */
export function useSealedAgentArtifactWindow(
  runId: string,
  messageId: string,
  purpose: string | null,
): UseSealedAgentArtifactWindowResult {
  const [state, setState] = useState<SealedAgentArtifactWindowState>({ status: 'idle' })
  const generationRef = useRef(0)
  const loadingRef = useRef(false)
  const accumulatedTextRef = useRef('')
  // The offset most recently requested (regardless of outcome) — lets a retry after a failed
  // LATER window re-request that same failed offset rather than assuming offset 0, which would
  // otherwise re-fetch and re-append the already-accumulated earlier text (duplication) while
  // never actually retrying the window that failed.
  const lastRequestedOffsetRef = useRef(0)
  const [renderedTriple, setRenderedTriple] = useState({ runId, messageId, purpose })

  if (renderedTriple.runId !== runId || renderedTriple.messageId !== messageId || renderedTriple.purpose !== purpose) {
    setRenderedTriple({ runId, messageId, purpose })
    setState({ status: 'idle' })
  }

  // accumulatedTextRef is never read during render (only inside fetchWindow's async
  // continuations), so clearing it here — alongside the other bookkeeping refs, and for the same
  // reason those are never touched in the render body itself — is safe and keeps the reset
  // co-located with the generation bump it must happen before any new fetch can start after.
  useLayoutEffect(() => {
    generationRef.current += 1
    loadingRef.current = false
    accumulatedTextRef.current = ''
    lastRequestedOffsetRef.current = 0
  }, [runId, messageId, purpose])

  const fetchWindow = useCallback(
    (fromOffset: number) => {
      if (loadingRef.current || purpose === null) {
        return
      }

      const generation = generationRef.current
      loadingRef.current = true
      lastRequestedOffsetRef.current = fromOffset
      setState({ status: 'loading' })

      sealedAgentArtifactWindowClient()
        .getSealedAgentArtifactWindow(runId, messageId, purpose, fromOffset, DEFAULT_MAX_BYTES)
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
    [runId, messageId, purpose],
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
