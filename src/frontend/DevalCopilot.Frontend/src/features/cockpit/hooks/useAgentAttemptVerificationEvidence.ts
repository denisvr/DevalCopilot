import { useCallback, useEffect, useLayoutEffect, useRef } from 'react'
import { agentAttemptVerificationEvidenceClient } from '../../../api/clients'
import { toAgentAttemptVerificationEvidence } from '../agentAttemptVerification'
import type { AgentAttemptVerificationEvidence, RecordedVerificationStreamDescriptor, VerificationStream } from '../agentAttemptVerification'
import type { AgentAttemptIdentityExpectation } from '../agentAttemptHistory'
import { useOwnedLifetime, useOwnedState } from './useOwnedLifetime'
import type { OwnedLifetime } from './useOwnedLifetime'

/** `read`: the request failed. `not-found`: the host no longer knows this Agent attempt. `invalid`: the answer is not coherent for it. */
export type AgentAttemptVerificationEvidenceFailure = 'read' | 'not-found' | 'invalid'

/** The selected attempt: the selection lifetime it belongs to, its run and the accepted history row's immutable identity. */
export interface AgentAttemptVerificationEvidenceTarget {
  selectionKey: string | null
  runId: string
  expected: AgentAttemptIdentityExpectation
}

/** One member's stream chosen for a manual read; only a stream whose accepted descriptor is Recorded can be chosen. */
export interface VerificationSelection {
  executionId: string
  stream: VerificationStream
  /**
   * The visit of this choice within its opening: choosing a stream after Hide or after another stream starts a new visit with a
   * distinct number, even for the stream chosen before, while choosing the stream that is already chosen keeps its visit.
   */
  visit: number
  descriptor: RecordedVerificationStreamDescriptor
}

export interface UseAgentAttemptVerificationEvidenceResult {
  /** Whether the section of the current selection is open. */
  open: boolean
  /** The lifetime of the current opening (null while closed), which every member/stream selection and window belongs to. */
  openingKey: string | null
  pending: boolean
  /** The validated answer of the current opening; never together with a failure. */
  evidence: AgentAttemptVerificationEvidence | null
  failure: AgentAttemptVerificationEvidenceFailure | null
  /** The stream chosen in the current opening, or null. */
  selection: VerificationSelection | null
  /** Opens the section and reads the metadata once; an open section is left as it is. */
  show: () => void
  /** Closes the opening that created this callback, discarding its answer, selection, failure and any read still in flight. */
  close: () => void
  /** Reads the same attempt again after a failure of this opening; nothing else ever repeats a read. */
  retry: () => void
  /** Chooses a stream of a member of the accepted answer of this opening; an unknown member or an unrecorded stream is ignored. */
  select: (executionId: string, stream: VerificationStream) => void
  /** Clears the choice of the visit that created this callback; a later visit, even of the same stream, is never cleared by it. */
  clearSelection: () => void
}

// What the attempt-selection lifetime owns: whether its section is open and how many times it was opened, so each opening is a lifetime.
interface OpeningFrame {
  open: boolean
  serial: number
}

// What one opening owns: its single metadata read in flight, the validated answer or a failure.
interface ReadFrame {
  pending: boolean
  evidence: AgentAttemptVerificationEvidence | null
  failure: AgentAttemptVerificationEvidenceFailure | null
}

interface ChosenStream {
  executionId: string
  stream: VerificationStream
  visit: number
}

// What one opening owns for its member/stream choice: the current visit, if any, and how many visits it started, so each visit has its
// own number and a callback of one visit can recognize, inside a queued update, that a later one replaced it.
interface ChoiceFrame {
  chosen: ChosenStream | null
  visits: number
}

const createClosed = (): OpeningFrame => ({ open: false, serial: 0 })

const createPending = (): ReadFrame => ({ pending: true, evidence: null, failure: null })

const createNoChoice = (): ChoiceFrame => ({ chosen: null, visits: 0 })

function isNotFound(caught: unknown): boolean {
  return typeof caught === 'object' && caught !== null && (caught as { status?: unknown }).status === 404
}

/**
 * The on-demand recorded verification membership of one selected CodeReviewer attempt (ADR-0052), independent of its evidence, its
 * collaboration inputs, its Agent artifacts and any message it produced. Two lifetimes own it: the attempt's selection (another
 * attempt, a return to an earlier one, another run or project, a history reload, closing the attempt or unmounting ends it and the
 * section with it) and each opening (closing then opening again is a new lifetime, and the stream chosen in it ends with it). Within an
 * opening every member/stream choice is a numbered visit whose Hide ends only that visit, and whose number scopes its output window. A
 * replacement derives its first frame in the same render, an older or ended read can neither overwrite nor settle a newer one, and the
 * callbacks of an ended lifetime start nothing. Nothing is read until the owner opens it; the answer is accepted only whole and only for
 * exactly the selected attempt; nothing is polled, retried automatically, cached across lifetimes, logged or stored.
 */
export function useAgentAttemptVerificationEvidence({
  selectionKey,
  runId,
  expected,
}: AgentAttemptVerificationEvidenceTarget): UseAgentAttemptVerificationEvidenceResult {
  const { attemptId, attemptNumber, role, provider, responseContract } = expected
  const selectionOwner = useOwnedLifetime(selectionKey)
  const [opening, commitOpening] = useOwnedState(selectionOwner, createClosed)
  const isOpen = selectionKey !== null && opening.open
  const openingKey = isOpen ? JSON.stringify([selectionKey, opening.serial]) : null
  const owner = useOwnedLifetime(openingKey)
  const [frame, commit] = useOwnedState(owner, createPending)
  const [choice, commitChoice] = useOwnedState(owner, createNoChoice)

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

    const isCurrent = owner.begin('evidence')
    inFlight.current = { owner, isCurrent }
    const settle = (update: () => ReadFrame) => {
      if (isCurrent()) {
        inFlight.current = null
        commit(update)
      }
    }

    commit(createPending)
    agentAttemptVerificationEvidenceClient()
      .readVerificationEvidence(runId, attemptId)
      .then(
        (answer) =>
          settle(() => {
            const accepted = toAgentAttemptVerificationEvidence(answer, {
              runId,
              attemptId,
              attemptNumber,
              role,
              provider,
              responseContract,
            })
            return accepted === null
              ? { pending: false, evidence: null, failure: 'invalid' }
              : { pending: false, evidence: accepted, failure: null }
          }),
        (caught: unknown) => settle(() => ({ pending: false, evidence: null, failure: isNotFound(caught) ? 'not-found' : 'read' })),
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

  const select = useCallback(
    (executionId: string, stream: VerificationStream) => {
      const current = displayed.current
      if (!owner.isActive() || owner.key === null || current === null || current.owner !== owner) {
        return
      }

      if (descriptorOf(current.frame.evidence, executionId, stream) === null) {
        return
      }

      // Decided against the queued choice, so a Hide queued earlier in the same batch makes this a new visit.
      commitChoice((previous) =>
        previous.chosen?.executionId === executionId && previous.chosen.stream === stream
          ? previous
          : { chosen: { executionId, stream, visit: previous.visits + 1 }, visits: previous.visits + 1 },
      )
    },
    [owner, commitChoice],
  )

  const evidence = isOpen ? frame.evidence : null
  const chosen = isOpen ? choice.chosen : null
  const descriptor = chosen === null ? null : descriptorOf(evidence, chosen.executionId, chosen.stream)
  const selection = chosen !== null && descriptor !== null ? { ...chosen, descriptor } : null

  // Hide belongs to the visit it was rendered for: it clears only while that visit is still the queued choice, so a retained Hide can
  // never end a later visit, including one queued before it in the same batch or a return to the same stream.
  const visit = selection?.visit ?? null
  const clearSelection = useCallback(() => {
    if (visit === null || !owner.isActive() || owner.key === null) {
      return
    }

    commitChoice((previous) => (previous.chosen?.visit === visit ? { ...previous, chosen: null } : previous))
  }, [owner, visit, commitChoice])

  return {
    open: isOpen,
    openingKey,
    pending: isOpen && frame.pending,
    evidence,
    failure: isOpen ? frame.failure : null,
    selection,
    show,
    close,
    retry,
    select,
    clearSelection,
  }
}

/** The Recorded descriptor of one member's stream in an accepted answer, or null for anything else. */
function descriptorOf(
  evidence: AgentAttemptVerificationEvidence | null,
  executionId: string,
  stream: VerificationStream,
): RecordedVerificationStreamDescriptor | null {
  if (evidence?.status !== 'Recorded') {
    return null
  }

  const member = evidence.members.find((candidate) => candidate.executionId === executionId)
  const descriptor = stream === 'stdout' ? member?.standardOutput : member?.standardError
  return descriptor?.status === 'Recorded' ? descriptor : null
}
