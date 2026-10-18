import { useCallback, useEffect, useLayoutEffect, useRef } from 'react'
import { agentAttemptInputsClient } from '../../../api/clients'
import { toAgentAttemptInputs } from '../agentAttemptInputs'
import type { AgentAttemptInputs } from '../agentAttemptInputs'
import type { AgentAttemptIdentityExpectation } from '../agentAttemptHistory'
import { useOwnedLifetime, useOwnedState } from './useOwnedLifetime'
import type { OwnedLifetime } from './useOwnedLifetime'

/** `read`: the request failed. `not-found`: the host no longer knows this Agent attempt. `invalid`: the answer is not coherent for it. */
export type AgentAttemptInputsFailure = 'read' | 'not-found' | 'invalid'

/** The selected attempt: the selection lifetime it belongs to, its run and the accepted history row's immutable identity. */
export interface AgentAttemptInputsTarget {
  selectionKey: string | null
  runId: string
  expected: AgentAttemptIdentityExpectation
}

export interface UseAgentAttemptInputsResult {
  /** Whether the inspector of the current selection is open. */
  open: boolean
  pending: boolean
  /** The validated answer of the current opening; never together with a failure. */
  inputs: AgentAttemptInputs | null
  failure: AgentAttemptInputsFailure | null
  /** Opens the inspector and reads once; an open inspector is left as it is. */
  show: () => void
  /** Closes the opening that created this callback, discarding its answer, failure and any read still in flight. */
  close: () => void
  /** Reads the same attempt again after a failure of this opening; nothing else ever repeats a read. */
  retry: () => void
}

// What the selection lifetime owns: whether its inspector is open and how many times it was opened, so each opening is a lifetime.
interface OpeningFrame {
  open: boolean
  serial: number
}

// What one opening owns: its single read in flight, the validated answer or a failure.
interface ReadFrame {
  pending: boolean
  inputs: AgentAttemptInputs | null
  failure: AgentAttemptInputsFailure | null
}

const createClosed = (): OpeningFrame => ({ open: false, serial: 0 })

const createPending = (): ReadFrame => ({ pending: true, inputs: null, failure: null })

function isNotFound(caught: unknown): boolean {
  return typeof caught === 'object' && caught !== null && (caught as { status?: unknown }).status === 404
}

/**
 * The on-demand recorded collaboration-input references of one selected Agent attempt (ADR-0051), independent of any message it produced
 * and of its evidence, facts and artifact reads. Two lifetimes own it: the attempt's selection (another attempt, a return to an earlier
 * one, another run or project, a history reload, closing the attempt or unmounting ends it and the inspector with it) and each opening
 * (closing then opening again is a new lifetime). A replacement derives its first frame in the same render, an older or ended read can
 * neither overwrite nor settle a newer one, and the callbacks of an ended lifetime start nothing: `close` is bound to the opening that
 * created it and guarded by its serial. Nothing is read until the owner opens it; the answer is accepted only whole and only for exactly
 * the selected attempt; nothing is polled, retried automatically, cached across lifetimes, logged or stored.
 */
export function useAgentAttemptInputs({ selectionKey, runId, expected }: AgentAttemptInputsTarget): UseAgentAttemptInputsResult {
  const { attemptId, attemptNumber, role, provider, responseContract } = expected
  const selectionOwner = useOwnedLifetime(selectionKey)
  const [opening, commitOpening] = useOwnedState(selectionOwner, createClosed)
  const isOpen = selectionKey !== null && opening.open
  const owner = useOwnedLifetime(isOpen ? JSON.stringify([selectionKey, opening.serial]) : null)
  const [frame, commit] = useOwnedState(owner, createPending)

  // The frame this render committed and the one read in flight, both keyed by their lifetime, so a callback retained from an earlier
  // lifetime can never act on a later one and two calls in the same tick send one request.
  const displayed = useRef<{ owner: OwnedLifetime; frame: ReadFrame } | null>(null)
  const inFlight = useRef<{ owner: OwnedLifetime; isCurrent: () => boolean } | null>(null)

  useLayoutEffect(() => {
    displayed.current = { owner, frame }
  })

  const show = useCallback(() => {
    if (!selectionOwner.isActive() || selectionOwner.key === null) {
      return
    }

    commitOpening((previous) => (previous.open ? previous : { open: true, serial: previous.serial + 1 }))
  }, [selectionOwner, commitOpening])

  const serial = opening.serial
  const close = useCallback(() => {
    if (!owner.isActive() || owner.key === null) {
      return
    }

    commitOpening((previous) => (previous.serial === serial && previous.open ? { ...previous, open: false } : previous))
  }, [owner, serial, commitOpening])

  const read = useCallback(() => {
    if (!owner.isActive() || owner.key === null) {
      return
    }

    const isCurrent = owner.begin('inputs')
    inFlight.current = { owner, isCurrent }
    const settle = (update: () => ReadFrame) => {
      if (isCurrent()) {
        inFlight.current = null
        commit(update)
      }
    }

    commit(createPending)
    agentAttemptInputsClient()
      .readAgentAttemptInputs(runId, attemptId)
      .then(
        (answer) =>
          settle(() => {
            const accepted = toAgentAttemptInputs(answer, { runId, attemptId, attemptNumber, role, provider, responseContract })
            return accepted === null
              ? { pending: false, inputs: null, failure: 'invalid' }
              : { pending: false, inputs: accepted, failure: null }
          }),
        (caught: unknown) => settle(() => ({ pending: false, inputs: null, failure: isNotFound(caught) ? 'not-found' : 'read' })),
      )
  }, [owner, commit, runId, attemptId, attemptNumber, role, provider, responseContract])

  useEffect(() => {
    read()
  }, [read])

  const retry = useCallback(() => {
    const current = displayed.current
    if (!owner.isActive() || owner.key === null || current === null || current.owner !== owner) {
      return
    }

    if (current.frame.failure === null || current.frame.pending || (inFlight.current?.owner === owner && inFlight.current.isCurrent())) {
      return
    }

    read()
  }, [owner, read])

  return {
    open: isOpen,
    pending: isOpen && frame.pending,
    inputs: isOpen ? frame.inputs : null,
    failure: isOpen ? frame.failure : null,
    show,
    close,
    retry,
  }
}
