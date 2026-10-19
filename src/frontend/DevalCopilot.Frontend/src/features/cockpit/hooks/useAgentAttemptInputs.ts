import { useCallback, useEffect, useLayoutEffect, useRef } from 'react'
import { agentAttemptInputsClient } from '../../../api/clients'
import { AGENT_ATTEMPT_INPUTS_MAX_LIMIT, AGENT_ATTEMPT_INPUTS_SHOWN, toAgentAttemptInputs } from '../agentAttemptInputs'
import type { AgentAttemptInputReference, AgentAttemptInputs } from '../agentAttemptInputs'
import type { AgentAttemptIdentityExpectation } from '../agentAttemptHistory'
import { useOwnedLifetime, useOwnedState } from './useOwnedLifetime'
import type { OwnedLifetime } from './useOwnedLifetime'

/** `read`: the request failed. `not-found`: the host no longer knows this Agent attempt. `invalid`: the answer is not coherent for it. */
export type AgentAttemptInputsFailure = 'read' | 'not-found' | 'invalid'

/** How many more loaded references one explicit local disclosure reveals (ADR-0053). */
export const AGENT_ATTEMPT_INPUTS_DISCLOSURE_STEP = 10

/** The selected attempt: the selection lifetime it belongs to, its run and the accepted history row's immutable identity. */
export interface AgentAttemptInputsTarget {
  selectionKey: string | null
  runId: string
  expected: AgentAttemptIdentityExpectation
}

/** One input chosen for an explicit content read; only a reference of the accepted answer of the current opening can be chosen. */
export interface AgentAttemptInputSelection {
  reference: AgentAttemptInputReference
  /**
   * The visit of this choice within its opening: choosing an input after Close or after another input starts a new visit with a
   * distinct number, even for the input chosen before, while choosing the input that is already chosen keeps its visit.
   */
  visit: number
}

export interface UseAgentAttemptInputsResult {
  /** Whether the inspector of the current selection is open. */
  open: boolean
  /** The lifetime of the current opening (null while closed), which the local disclosure and every input visit belong to. */
  openingKey: string | null
  pending: boolean
  /** The validated answer of the current opening; never together with a failure. */
  inputs: AgentAttemptInputs | null
  failure: AgentAttemptInputsFailure | null
  /** How many of the loaded references of this opening are revealed locally (0 unless the answer is Recorded). */
  visibleCount: number
  /** The input chosen in the current opening, or null. */
  selection: AgentAttemptInputSelection | null
  /** Opens the inspector and reads once; an open inspector is left as it is. */
  show: () => void
  /** Closes the opening that created this callback, discarding its answer, disclosure, choice, failure and any read still in flight. */
  close: () => void
  /** Reads the same attempt again after a failure of this opening; nothing else ever repeats a read. */
  retry: () => void
  /** Reveals up to ten more already loaded references of this opening, without a request; a callback of an ended opening does nothing. */
  showMore: () => void
  /** Chooses one reference of the accepted answer of this opening; an unknown input is ignored and the current one is kept as it is. */
  select: (collaborationMessageId: string) => void
  /** Clears the choice of the visit that created this callback; a later visit, even of the same input, is never cleared by it. */
  clearSelection: () => void
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

// What one opening owns for its local disclosure: how many loaded references are revealed.
interface DisclosureFrame {
  visible: number
}

// What one opening owns for its input choice: the current visit, if any, and how many visits it started, so each visit has its own number
// and a callback of one visit can recognize, inside a queued update, that a later one replaced it.
interface ChoiceFrame {
  chosen: { collaborationMessageId: string; visit: number } | null
  visits: number
}

const createClosed = (): OpeningFrame => ({ open: false, serial: 0 })

const createPending = (): ReadFrame => ({ pending: true, inputs: null, failure: null })

const createDisclosure = (): DisclosureFrame => ({ visible: AGENT_ATTEMPT_INPUTS_SHOWN })

const createNoChoice = (): ChoiceFrame => ({ chosen: null, visits: 0 })

function isNotFound(caught: unknown): boolean {
  return typeof caught === 'object' && caught !== null && (caught as { status?: unknown }).status === 404
}

function referenceOf(inputs: AgentAttemptInputs | null, collaborationMessageId: string): AgentAttemptInputReference | null {
  return inputs?.status === 'Recorded'
    ? (inputs.inputs.find((reference) => reference.collaborationMessageId === collaborationMessageId) ?? null)
    : null
}

/**
 * The on-demand recorded collaboration-input references of one selected Agent attempt (ADR-0051/0053), independent of any message it
 * produced and of its evidence, facts and artifact reads. Two lifetimes own it: the attempt's selection (another attempt, a return to an
 * earlier one, another run or project, a history reload, closing the attempt or unmounting ends it and the inspector with it) and each
 * opening (closing then opening again is a new lifetime). Each opening reads the whole admitted set once with `limit=64`, reveals ten
 * references and then further local chunks of ten only on request, without another request; its disclosure and input choice end with
 * it. Within an opening every input choice is a numbered visit whose Close ends only that visit and whose number scopes its content read.
 * A replacement derives its first frame in the same render, an older or ended read can neither overwrite nor settle a newer one, and the
 * callbacks of an ended lifetime start nothing. Nothing is read until the owner opens it; the answer is accepted only whole and only for
 * exactly the selected attempt and requested limit; no content is prefetched, and nothing is polled, retried automatically, cached across
 * lifetimes, logged or stored.
 */
export function useAgentAttemptInputs({ selectionKey, runId, expected }: AgentAttemptInputsTarget): UseAgentAttemptInputsResult {
  const { attemptId, attemptNumber, role, provider, responseContract } = expected
  const selectionOwner = useOwnedLifetime(selectionKey)
  const [opening, commitOpening] = useOwnedState(selectionOwner, createClosed)
  const isOpen = selectionKey !== null && opening.open
  const openingKey = isOpen ? JSON.stringify([selectionKey, opening.serial]) : null
  const owner = useOwnedLifetime(openingKey)
  const [frame, commit] = useOwnedState(owner, createPending)
  const [disclosure, commitDisclosure] = useOwnedState(owner, createDisclosure)
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
      .readAgentAttemptInputs(runId, attemptId, AGENT_ATTEMPT_INPUTS_MAX_LIMIT)
      .then(
        (answer) =>
          settle(() => {
            const accepted = toAgentAttemptInputs(
              answer,
              { runId, attemptId, attemptNumber, role, provider, responseContract },
              AGENT_ATTEMPT_INPUTS_MAX_LIMIT,
            )
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

  const showMore = useCallback(() => {
    const current = displayed.current
    if (!owner.isActive() || owner.key === null || current === null || current.owner !== owner) {
      return
    }

    const loaded = current.frame.inputs
    if (loaded?.status !== 'Recorded') {
      return
    }

    commitDisclosure((previous) => ({
      visible: Math.min(previous.visible + AGENT_ATTEMPT_INPUTS_DISCLOSURE_STEP, Math.max(previous.visible, loaded.inputs.length)),
    }))
  }, [owner, commitDisclosure])

  const select = useCallback(
    (collaborationMessageId: string) => {
      const current = displayed.current
      if (!owner.isActive() || owner.key === null || current === null || current.owner !== owner) {
        return
      }

      if (referenceOf(current.frame.inputs, collaborationMessageId) === null) {
        return
      }

      // Decided against the queued choice, so a Close queued earlier in the same batch makes this a new visit.
      commitChoice((previous) =>
        previous.chosen?.collaborationMessageId === collaborationMessageId
          ? previous
          : { chosen: { collaborationMessageId, visit: previous.visits + 1 }, visits: previous.visits + 1 },
      )
    },
    [owner, commitChoice],
  )

  const inputs = isOpen ? frame.inputs : null
  const chosen = isOpen ? choice.chosen : null
  const reference = chosen === null ? null : referenceOf(inputs, chosen.collaborationMessageId)
  const selection = chosen !== null && reference !== null ? { reference, visit: chosen.visit } : null

  // Close belongs to the visit it was rendered for: it clears only while that visit is still the queued choice, so a retained Close can
  // never end a later visit, including one queued before it in the same batch or a return to the same input.
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
    inputs,
    failure: isOpen ? frame.failure : null,
    visibleCount: inputs?.status === 'Recorded' ? Math.min(disclosure.visible, inputs.inputs.length) : 0,
    selection,
    show,
    close,
    retry,
    showMore,
    select,
    clearSelection,
  }
}
