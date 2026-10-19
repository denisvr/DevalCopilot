import { useCallback, useEffect, useLayoutEffect, useRef } from 'react'
import { agentAttemptOutputMessageClient } from '../../../api/clients'
import { toAgentAttemptOutputMessage } from '../agentAttemptOutputMessage'
import type { AgentAttemptOutputMessage } from '../agentAttemptOutputMessage'
import type { AgentAttemptIdentityExpectation } from '../agentAttemptHistory'
import type { AgentAttemptOutputSelection } from './useAgentAttemptOutputs'
import { useOwnedLifetime, useOwnedState } from './useOwnedLifetime'
import type { OwnedLifetime } from './useOwnedLifetime'

/** `read`: the request failed. `not-found`: the host no longer proves this output. `invalid`: the answer is not coherent for it. */
export type AgentAttemptOutputMessageFailure = 'read' | 'not-found' | 'invalid'

/** The opening the selection belongs to, the selected attempt and the chosen output visit (null: nothing chosen). */
export interface AgentAttemptOutputMessageTarget {
  openingKey: string | null
  runId: string
  expected: AgentAttemptIdentityExpectation
  selection: AgentAttemptOutputSelection | null
}

export interface UseAgentAttemptOutputMessageResult {
  pending: boolean
  /** The validated answer of the current visit; never together with a failure. */
  answer: AgentAttemptOutputMessage | null
  failure: AgentAttemptOutputMessageFailure | null
  /** Reads the same output of the same visit again after a failure; nothing else ever repeats a read. */
  retry: () => void
}

// What one (opening, run, attempt, output, visit) lifetime owns: its single read in flight, the validated answer or a failure.
interface MessageFrame {
  pending: boolean
  answer: AgentAttemptOutputMessage | null
  failure: AgentAttemptOutputMessageFailure | null
}

const createPending = (): MessageFrame => ({ pending: true, answer: null, failure: null })

function isNotFound(caught: unknown): boolean {
  return typeof caught === 'object' && caught !== null && (caught as { status?: unknown }).status === 404
}

/**
 * The explicit content read of one selected recorded output (ADR-0054), scoped to exactly one `(opening, run, attempt, output, visit)`
 * lifetime. Choosing an output reads it once; another output, a Close and reselection (even of the same output in one batch), another
 * opening, attempt, run or project, a reload or unmounting is a new or ended lifetime whose first frame is derived in the same render, so
 * a change can never show another visit's content, a late answer of an ended lifetime settles nothing and its retained `retry` starts
 * nothing. The answer is accepted only whole and only for exactly the selected reference; a failure is retried only on request. Nothing
 * is prefetched, polled, followed to a reply, attempt or artifact, cached across lifetimes, logged or stored.
 */
export function useAgentAttemptOutputMessage({
  openingKey,
  runId,
  expected,
  selection,
}: AgentAttemptOutputMessageTarget): UseAgentAttemptOutputMessageResult {
  const { attemptId } = expected
  const messageId = selection?.reference.collaborationMessageId
  const visit = selection?.visit
  const owner = useOwnedLifetime(
    openingKey === null || selection === null ? null : JSON.stringify([openingKey, runId, attemptId, messageId, visit]),
  )
  const [frame, commit] = useOwnedState(owner, createPending)

  // The frame this render committed, the read in flight and the target it was rendered for, all read through refs so a callback retained
  // from an earlier lifetime can never act on a later one, two calls in the same tick send one request, and a lifetime reads only once.
  const displayed = useRef<{ owner: OwnedLifetime; frame: MessageFrame } | null>(null)
  const inFlight = useRef<{ owner: OwnedLifetime; isCurrent: () => boolean } | null>(null)
  const target = useRef({ runId, expected, selection })

  useLayoutEffect(() => {
    displayed.current = { owner, frame }
    target.current = { runId, expected, selection }
  })

  const read = useCallback(() => {
    const { runId: run, expected: attempt, selection: chosen } = target.current
    if (!owner.isActive() || owner.key === null || chosen === null) {
      return
    }

    const isCurrent = owner.begin('output-message')
    inFlight.current = { owner, isCurrent }
    const settle = (update: () => MessageFrame) => {
      if (isCurrent()) {
        inFlight.current = null
        commit(update)
      }
    }

    const { reference } = chosen
    commit(createPending)
    agentAttemptOutputMessageClient()
      .readAgentAttemptOutputMessage(run, attempt.attemptId, reference.collaborationMessageId)
      .then(
        (answer) =>
          settle(() => {
            const accepted = toAgentAttemptOutputMessage(answer, { ...attempt, runId: run, reference })
            return accepted === null
              ? { pending: false, answer: null, failure: 'invalid' }
              : { pending: false, answer: accepted, failure: null }
          }),
        (caught: unknown) => settle(() => ({ pending: false, answer: null, failure: isNotFound(caught) ? 'not-found' : 'read' })),
      )
  }, [owner, commit])

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

  const active = owner.key !== null
  return {
    pending: active && frame.pending,
    answer: active ? frame.answer : null,
    failure: active ? frame.failure : null,
    retry,
  }
}
