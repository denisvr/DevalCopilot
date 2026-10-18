import { useCallback, useEffect, useLayoutEffect, useRef } from 'react'
import type { AgentAttemptEvidenceResponse } from '../../../api/clients'
import { agentAttemptEvidenceClient } from '../../../api/clients'
import { toAgentAttemptEvidence } from '../agentAttemptHistory'
import type { AgentAttemptIdentityExpectation } from '../agentAttemptHistory'
import { useOwnedLifetime, useOwnedState } from './useOwnedLifetime'
import type { OwnedLifetime } from './useOwnedLifetime'

export type AgentAttemptEvidenceState =
  | { status: 'loading' }
  | { status: 'success'; evidence: AgentAttemptEvidenceResponse }
  // The attempt exists but its persisted identity could not be proven coherent: nothing else about it is disclosed, and it is never
  // rendered as ordinary evidence.
  | { status: 'identity-invalid' }
  // The attempt is not (or no longer) an Agent attempt of this run.
  | { status: 'not-found' }
  // `read`: the request failed. `invalid`: the host answered, but not for exactly the selected attempt; none of it is shown.
  | { status: 'error'; failure: 'read' | 'invalid'; message: string }

export interface UseAgentAttemptEvidenceResult {
  state: AgentAttemptEvidenceState
  /** Reads the same selected attempt again after a failure; a no-op once its selection has ended. */
  retry: () => void
}

/** The selected attempt: the selection lifetime it belongs to, its run and what the answer must match from its accepted history row. */
export interface AgentAttemptEvidenceTarget {
  selectionKey: string | null
  runId: string
  /** The row's exact identity and number and its immutable role, provider and response contract (never its status or times). */
  expected: AgentAttemptIdentityExpectation
}

const MESSAGES = {
  read: 'The attempt evidence could not be loaded.',
  invalid: 'The host answered with attempt evidence that is not coherent for this attempt, so none of it is shown.',
} as const

const createLoading = (): AgentAttemptEvidenceState => ({ status: 'loading' })

function isNotFound(caught: unknown): boolean {
  return typeof caught === 'object' && caught !== null && (caught as { status?: unknown }).status === 404
}

/**
 * Bounded evidence metadata for exactly one selected `(runId, attemptId)` pair, read once when its selection lifetime begins and again
 * only through `retry`. The answer must name the selected identity and number and, when verified, the same immutable role, provider and
 * response contract before anything of it is shown or used to offer an artifact purpose; its status and times may have progressed. Another selection, a close, another run or unmounting ends the lifetime: its late answer settles nothing and its
 * retained `retry` starts nothing. Holds metadata only — never artifact text — and writes nothing to browser storage or the URL.
 */
export function useAgentAttemptEvidence({ selectionKey, runId, expected }: AgentAttemptEvidenceTarget): UseAgentAttemptEvidenceResult {
  const { attemptId, attemptNumber, role, provider, responseContract } = expected
  const owner = useOwnedLifetime(selectionKey)
  const [state, commit] = useOwnedState(owner, createLoading)
  const displayed = useRef<{ owner: OwnedLifetime; state: AgentAttemptEvidenceState } | null>(null)

  useLayoutEffect(() => {
    displayed.current = { owner, state }
  })

  const read = useCallback(() => {
    if (!owner.isActive() || owner.key === null) {
      return
    }

    const isCurrent = owner.begin('evidence')
    commit(createLoading)
    agentAttemptEvidenceClient()
      .getAgentAttemptEvidence(runId, attemptId)
      .then(
        (response) => {
          if (isCurrent()) {
            const answer = toAgentAttemptEvidence(response, { attemptId, attemptNumber, role, provider, responseContract })
            commit(() => answer ?? { status: 'error', failure: 'invalid', message: MESSAGES.invalid })
          }
        },
        (caught: unknown) => {
          if (isCurrent()) {
            commit(() => (isNotFound(caught) ? { status: 'not-found' } : { status: 'error', failure: 'read', message: MESSAGES.read }))
          }
        },
      )
  }, [owner, commit, runId, attemptId, attemptNumber, role, provider, responseContract])

  useEffect(() => {
    read()
  }, [read])

  // Only a failed read of this very selection is read again; a retained retry of an ended or loading selection starts nothing.
  const retry = useCallback(() => {
    const current = displayed.current
    if (current !== null && current.owner === owner && current.state.status === 'error') {
      read()
    }
  }, [owner, read])

  return { state, retry }
}
