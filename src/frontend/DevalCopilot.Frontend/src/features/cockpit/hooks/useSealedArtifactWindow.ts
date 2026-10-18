import { useCallback, useLayoutEffect, useRef } from 'react'
import type { SealedAgentArtifactWindowResponse } from '../../../api/clients'
import { useOwnedLifetime, useOwnedState } from './useOwnedLifetime'
import type { OwnedLifetime } from './useOwnedLifetime'

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
   * already loading, before a purpose is selected, or retained from an ended scope or purpose is a no-op. */
  loadNextWindow: () => void
}

export type SealedWindowRequest = (
  purpose: string,
  fromOffset: number,
  maxBytes: number,
) => Promise<SealedAgentArtifactWindowResponse>

// What one (scope, purpose) lifetime owns: the displayed state, the text accumulated across its windows and the offset it last requested.
interface WindowFrame {
  state: SealedAgentArtifactWindowState
  accumulated: string
  lastRequestedOffset: number
}

const createWindowFrame = (): WindowFrame => ({ state: { status: 'idle' }, accumulated: '', lastRequestedOffset: 0 })

const ERROR_MESSAGE = 'This artifact window is unavailable.'

function settleResponse(previous: WindowFrame, response: SealedAgentArtifactWindowResponse, fromOffset: number): WindowFrame {
  switch (response.status) {
    case 'Ok': {
      const accumulated = previous.accumulated + (response.text ?? '')
      const nextOffset = response.nextOffset ?? fromOffset
      const totalLengthSoFar = response.totalLengthSoFar ?? 0
      return {
        ...previous,
        accumulated,
        state: {
          status: 'loaded',
          text: accumulated,
          nextOffset,
          totalLengthSoFar,
          isComplete: nextOffset >= totalLengthSoFar,
          truncated: response.truncated ?? null,
        },
      }
    }
    case 'Missing':
      return { ...previous, state: { status: 'missing' } }
    case 'IntegrityMismatch':
      return { ...previous, state: { status: 'integrity-mismatch' } }
    case 'AttemptIdentityInvalid':
      return { ...previous, state: { status: 'identity-invalid' } }
    case 'NoAgentEvidence':
    case 'AttemptLinkBroken':
    case 'PurposeNotAllowlisted':
    case 'ArtifactNotFound':
      return { ...previous, state: { status: 'unavailable' } }
    default:
      // A status this client does not recognize is never rendered as if it were a known, safe state — reject it explicitly, exactly
      // like the collaboration-evidence hook.
      return { ...previous, state: { status: 'error', message: ERROR_MESSAGE } }
  }
}

/**
 * Fetches bounded, verified text windows of one sealed Agent-attempt artifact, scoped to exactly one `(scopeKey, purpose)` pair.
 * `scopeKey` identifies the run and the message or attempt (and, for a selection, its lifetime) the caller resolved the artifact
 * through, and `request` performs that route's fetch; the accumulation and retry behavior is identical for every route. State is
 * on-demand (idle until first requested) and every committed scope or purpose — including a return to an earlier one — is a new owned
 * lifetime whose first frame is derived idle in the same render, so a change can never flash a previous purpose's or scope's text. A
 * late answer of an ended lifetime settles nothing, and its retained `loadNextWindow` starts nothing.
 *
 * Fetched text is accumulated across the windows of one lifetime only and never survives a purpose, attempt, or run switch. Never
 * persists fetched text to `localStorage`/`sessionStorage`/the URL, and never logs it.
 *
 * A failed request for a LATER window (one requested after at least one window already loaded) can be retried without duplicating or
 * losing text: the failed offset is kept beside the state, a failure never touches the accumulated text, and retrying re-requests that
 * same offset rather than restarting from 0.
 */
export function useSealedArtifactWindow(
  scopeKey: string,
  purpose: string | null,
  request: SealedWindowRequest,
): UseSealedAgentArtifactWindowResult {
  const owner = useOwnedLifetime(purpose === null ? null : JSON.stringify([scopeKey, purpose]))
  const [frame, commit] = useOwnedState(owner, createWindowFrame)

  // The frame this render committed and the window read in flight, both keyed by their lifetime, so a retained callback can never act
  // on a later lifetime and two calls in the same tick send one request.
  const displayed = useRef<{ owner: OwnedLifetime; frame: WindowFrame } | null>(null)
  const inFlight = useRef<{ owner: OwnedLifetime; isCurrent: () => boolean } | null>(null)

  useLayoutEffect(() => {
    displayed.current = { owner, frame }
  })

  const fetchWindow = useCallback(
    (fromOffset: number) => {
      if (purpose === null || !owner.isActive() || owner.key === null || (inFlight.current?.owner === owner && inFlight.current.isCurrent())) {
        return
      }

      const isCurrent = owner.begin('window')
      inFlight.current = { owner, isCurrent }
      const settle = (update: (previous: WindowFrame) => WindowFrame) => {
        if (isCurrent()) {
          inFlight.current = null
          commit(update)
        }
      }

      commit((previous) => ({ ...previous, state: { status: 'loading' }, lastRequestedOffset: fromOffset }))
      request(purpose, fromOffset, SEALED_WINDOW_MAX_BYTES).then(
        (response: SealedAgentArtifactWindowResponse) => settle((previous) => settleResponse(previous, response, fromOffset)),
        () => settle((previous) => ({ ...previous, state: { status: 'error', message: ERROR_MESSAGE } })),
      )
    },
    [owner, commit, purpose, request],
  )

  const loadNextWindow = useCallback(() => {
    const current = displayed.current
    if (current === null || current.owner !== owner) {
      return
    }

    const { state, lastRequestedOffset } = current.frame
    if (state.status === 'idle') {
      fetchWindow(0)
    } else if (state.status === 'loaded' && !state.isComplete) {
      fetchWindow(state.nextOffset)
    } else if (state.status === 'error') {
      // Retries the exact offset that failed, preserving any earlier successfully-accumulated text (never touched on failure) — never
      // restarts at offset 0, which would re-fetch and re-append already-accumulated text and duplicate it.
      fetchWindow(lastRequestedOffset)
    }
  }, [owner, fetchWindow])

  return { state: frame.state, loadNextWindow }
}
