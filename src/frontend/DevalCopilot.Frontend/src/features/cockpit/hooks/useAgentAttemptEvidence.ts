import { useCallback, useEffect, useRef, useState } from 'react'
import type { AgentAttemptEvidenceResponse } from '../../../api/clients'
import { agentAttemptEvidenceClient } from '../../../api/clients'

export type AgentAttemptEvidenceState =
  | { status: 'loading' }
  | { status: 'success'; evidence: AgentAttemptEvidenceResponse }
  // The attempt exists but its persisted identity could not be proven coherent: nothing else about
  // it is disclosed, and it is never rendered as ordinary evidence.
  | { status: 'identity-invalid' }
  // The attempt is not (or no longer) an Agent attempt of this run.
  | { status: 'not-found' }
  | { status: 'error'; message: string }

export interface UseAgentAttemptEvidenceResult {
  state: AgentAttemptEvidenceState
  retry: () => void
}

const EVIDENCE_ERROR_MESSAGE = 'The attempt evidence could not be loaded.'

function isNotFound(caught: unknown): boolean {
  return typeof caught === 'object' && caught !== null && (caught as { status?: unknown }).status === 404
}

/**
 * Fetches bounded evidence metadata for exactly one `(runId, attemptId)` pair when this hook
 * mounts, so the parent mounts it (keyed by run and attempt) only once the owner selects an attempt.
 * A response for a previous run, attempt, or mount can never be applied. Holds metadata only —
 * never artifact text — and never writes anything to browser storage or the URL.
 */
export function useAgentAttemptEvidence(runId: string, attemptId: string): UseAgentAttemptEvidenceResult {
  const [state, setState] = useState<AgentAttemptEvidenceState>({ status: 'loading' })
  const generationRef = useRef(0)

  const request = useCallback(
    (generation: number) => {
      agentAttemptEvidenceClient()
        .getAgentAttemptEvidence(runId, attemptId)
        .then((evidence: AgentAttemptEvidenceResponse) => {
          if (generationRef.current !== generation) {
            return
          }

          setState(evidence.identityValid === true ? { status: 'success', evidence } : { status: 'identity-invalid' })
        })
        .catch((caught: unknown) => {
          if (generationRef.current === generation) {
            setState(isNotFound(caught) ? { status: 'not-found' } : { status: 'error', message: EVIDENCE_ERROR_MESSAGE })
          }
        })
    },
    [runId, attemptId],
  )

  useEffect(() => {
    generationRef.current += 1
    request(generationRef.current)
    return () => {
      generationRef.current += 1
    }
  }, [request])

  const retry = useCallback(() => {
    generationRef.current += 1
    setState({ status: 'loading' })
    request(generationRef.current)
  }, [request])

  return { state, retry }
}
