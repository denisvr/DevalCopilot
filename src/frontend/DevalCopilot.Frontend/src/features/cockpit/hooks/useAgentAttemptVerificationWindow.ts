import { useCallback, useLayoutEffect, useRef } from 'react'
import { agentAttemptVerificationWindowClient } from '../../../api/clients'
import {
  VERIFICATION_MIN_WINDOW_BYTES,
  VERIFICATION_VIEW_BUDGET_BYTES,
  VERIFICATION_WINDOW_BYTES,
  decideVerificationWindowAnswer,
} from '../agentAttemptVerification'
import type { VerificationWindowRequestFacts } from '../agentAttemptVerification'
import type { VerificationSelection } from './useAgentAttemptVerificationEvidence'
import { useOwnedLifetime, useOwnedState } from './useOwnedLifetime'
import type { OwnedLifetime } from './useOwnedLifetime'

/** `read`: the request failed. `not-found`: the host no longer proves this member. `invalid`: the answer is not coherent for the request. */
export type VerificationWindowFailure = 'read' | 'not-found' | 'invalid'

export type VerificationWindowState =
  | { status: 'idle' }
  // The text of the windows already accepted stays visible while the next one loads.
  | { status: 'loading'; text: string }
  | {
      status: 'loaded'
      text: string
      /** The UTF-8 byte cursor after the last accepted window: the source bytes this view retains. */
      nextOffset: number
      totalLength: number
      /** The whole recorded output has been read. */
      isComplete: boolean
      /** The local view budget ends before the output does; the output itself is longer, not ended. */
      viewLimitReached: boolean
    }
  | { status: 'unavailable' }
  | { status: 'not-recorded' }
  | { status: 'missing' }
  | { status: 'integrity-mismatch' }
  | { status: 'error'; failure: VerificationWindowFailure; text: string }

/** The opening the selection belongs to, the selected attempt and the chosen member stream (null: nothing chosen). */
export interface VerificationWindowTarget {
  openingKey: string | null
  runId: string
  attemptId: string
  selection: VerificationSelection | null
}

export interface UseAgentAttemptVerificationWindowResult {
  state: VerificationWindowState
  /**
   * Reads the first window when idle, the next one after an accepted window that did not end the output or the local view budget, or
   * the same window again after a failure. A call while loading, retained from an ended visit or opening, or after a closed
   * status is a no-op. Nothing else ever reads: there is no automatic paging, polling or retry.
   */
  load: () => void
}

// What one (opening, member, stream, visit) lifetime owns: the displayed state, the text of its accepted windows and the cursor of its next
// window, which a failed or rejected read never advances so a retry requests that same offset.
interface WindowFrame {
  state: VerificationWindowState
  accumulated: string
  cursor: number
}

const createWindowFrame = (): WindowFrame => ({ state: { status: 'idle' }, accumulated: '', cursor: 0 })

function isNotFound(caught: unknown): boolean {
  return typeof caught === 'object' && caught !== null && (caught as { status?: unknown }).status === 404
}

function settleAnswer(previous: WindowFrame, answer: unknown, facts: VerificationWindowRequestFacts): WindowFrame {
  const decided = decideVerificationWindowAnswer(answer, facts)
  switch (decided.kind) {
    case 'ok': {
      const text = previous.accumulated + decided.text
      const isComplete = decided.nextOffset === decided.totalLength
      return {
        accumulated: text,
        cursor: decided.nextOffset,
        state: {
          status: 'loaded',
          text,
          nextOffset: decided.nextOffset,
          totalLength: decided.totalLength,
          isComplete,
          viewLimitReached: !isComplete && VERIFICATION_VIEW_BUDGET_BYTES - decided.nextOffset < VERIFICATION_MIN_WINDOW_BYTES,
        },
      }
    }
    case 'incoherent':
      // An unrecognized or incoherent answer is never rendered as a known state: none of it is used, and the accepted text and cursor stay.
      return { ...previous, state: { status: 'error', failure: 'invalid', text: previous.accumulated } }
    default:
      return { ...previous, state: { status: decided.kind } }
  }
}

/**
 * Manual, bounded windows of one recorded verification member's stdout or stderr (ADR-0052), scoped to exactly one
 * `(opening, member, stream, visit)` lifetime. Every committed change of the opening, the chosen member or stream or its visit, including
 * a return to an earlier one and a Hide and reselection of the same stream in one batch, is a new owned lifetime whose first frame is
 * derived idle in the same render, so a change can never flash another visit's text, and a late answer of an ended lifetime settles
 * nothing while its retained `load` starts nothing.
 *
 * The text of the accepted windows of one lifetime is accumulated and never survives a selection change, closing or reopening. A
 * request is capped by 16 KiB and by what remains of the 256 KiB source-byte view budget, and nothing is requested once fewer than 64
 * bytes of that budget remain; the state then discloses a local view limit, never an end of the output. Each answer is decided whole
 * against its own request and the descriptor the selection was made from; only an accepted window appends text or advances the
 * cursor. Nothing is persisted to storage or the URL, logged, downloaded or exported.
 */
export function useAgentAttemptVerificationWindow({
  openingKey,
  runId,
  attemptId,
  selection,
}: VerificationWindowTarget): UseAgentAttemptVerificationWindowResult {
  const executionId = selection?.executionId
  const stream = selection?.stream
  const visit = selection?.visit
  const owner = useOwnedLifetime(
    openingKey === null || selection === null ? null : JSON.stringify([openingKey, runId, attemptId, executionId, stream, visit]),
  )
  const [frame, commit] = useOwnedState(owner, createWindowFrame)

  // The frame this render committed and the window read in flight, both keyed by their lifetime, so a retained callback can never act
  // on a later lifetime and two calls in the same tick send one request.
  const displayed = useRef<{ owner: OwnedLifetime; frame: WindowFrame } | null>(null)
  const inFlight = useRef<{ owner: OwnedLifetime; isCurrent: () => boolean } | null>(null)
  const chosen = useRef(selection)

  useLayoutEffect(() => {
    displayed.current = { owner, frame }
    chosen.current = selection
  })

  const load = useCallback(() => {
    const current = displayed.current
    const target = chosen.current
    if (
      current === null
      || current.owner !== owner
      || target === null
      || !owner.isActive()
      || owner.key === null
      || (inFlight.current?.owner === owner && inFlight.current.isCurrent())
    ) {
      return
    }

    const { state, cursor } = current.frame
    const readable = state.status === 'idle'
      || state.status === 'error'
      || (state.status === 'loaded' && !state.isComplete && !state.viewLimitReached)
    const remaining = VERIFICATION_VIEW_BUDGET_BYTES - cursor
    if (!readable || remaining < VERIFICATION_MIN_WINDOW_BYTES) {
      return
    }

    const facts: VerificationWindowRequestFacts = {
      runId,
      attemptId,
      executionId: target.executionId,
      stream: target.stream,
      descriptor: target.descriptor,
      fromOffset: cursor,
      maxBytes: Math.min(VERIFICATION_WINDOW_BYTES, remaining),
    }
    const isCurrent = owner.begin('window')
    inFlight.current = { owner, isCurrent }
    const settle = (update: (previous: WindowFrame) => WindowFrame) => {
      if (isCurrent()) {
        inFlight.current = null
        commit(update)
      }
    }

    commit((previous) => ({ ...previous, state: { status: 'loading', text: previous.accumulated } }))
    agentAttemptVerificationWindowClient()
      .readVerificationWindow(runId, attemptId, facts.executionId, facts.stream, facts.fromOffset, facts.maxBytes)
      .then(
        (answer: unknown) => settle((previous) => settleAnswer(previous, answer, facts)),
        (caught: unknown) =>
          settle((previous) => ({
            ...previous,
            state: { status: 'error', failure: isNotFound(caught) ? 'not-found' : 'read', text: previous.accumulated },
          })),
      )
  }, [owner, commit, runId, attemptId])

  return { state: frame.state, load }
}
