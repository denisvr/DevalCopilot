import { useCallback, useLayoutEffect, useRef } from 'react'
import type { SealedAgentArtifactWindowResponse } from '../../../api/clients'
import { decideSealedWindowAnswer } from '../sealedArtifactWindow'
import type { SealedWindowRequestFacts } from '../sealedArtifactWindow'
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

// What one (scope, purpose) lifetime owns: the displayed state, the text of its accepted windows, the cursor of its next window (which a
// failed or rejected read never advances, so a retry requests that same offset) and the sealed length its first accepted window established.
interface WindowFrame {
  state: SealedAgentArtifactWindowState
  accumulated: string
  cursor: number
  sealedTotal: number | null
}

const createWindowFrame = (): WindowFrame => ({ state: { status: 'idle' }, accumulated: '', cursor: 0, sealedTotal: null })

const ERROR_MESSAGE = 'This artifact window is unavailable.'

function settleResponse(previous: WindowFrame, response: SealedAgentArtifactWindowResponse, request: SealedWindowRequestFacts): WindowFrame {
  const answer = decideSealedWindowAnswer(response, request)
  switch (answer.kind) {
    case 'ok': {
      const accumulated = previous.accumulated + answer.text
      return {
        accumulated,
        cursor: answer.nextOffset,
        sealedTotal: answer.totalLength,
        state: {
          status: 'loaded',
          text: accumulated,
          nextOffset: answer.nextOffset,
          totalLengthSoFar: answer.totalLength,
          isComplete: answer.nextOffset === answer.totalLength,
          truncated: answer.truncated,
        },
      }
    }
    case 'incoherent':
      // An unrecognized status or an incoherent window is never rendered as if it were a known, safe state: none of it is used, and the
      // accepted text and cursor stay as they were.
      return { ...previous, state: { status: 'error', message: ERROR_MESSAGE } }
    default:
      return { ...previous, state: { status: answer.kind } }
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
 * Each answer is decided whole by `decideSealedWindowAnswer` against the offset and byte cap it was requested with and the sealed length
 * an earlier accepted window established; only an accepted `Ok` window appends text or advances the cursor. A failed request or an
 * incoherent answer shows one fixed error, contributes no text or cursor, and keeps the accepted text internally, so retrying re-requests
 * that same offset and appends exactly once after a valid answer — never restarting from 0.
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
    (facts: SealedWindowRequestFacts) => {
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

      commit((previous) => ({ ...previous, state: { status: 'loading' } }))
      request(purpose, facts.fromOffset, facts.maxBytes).then(
        (response: SealedAgentArtifactWindowResponse) => settle((previous) => settleResponse(previous, response, facts)),
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

    // The next window always starts at the lifetime's accepted cursor: 0 before any window, the last accepted window's end after one,
    // and, after a failed or rejected read, the exact offset that read requested — never offset 0 again, which would re-append text.
    const { state, cursor, sealedTotal } = current.frame
    if (state.status === 'idle' || state.status === 'error' || (state.status === 'loaded' && !state.isComplete)) {
      fetchWindow({ fromOffset: cursor, maxBytes: SEALED_WINDOW_MAX_BYTES, sealedTotal })
    }
  }, [owner, fetchWindow])

  return { state: frame.state, loadNextWindow }
}
