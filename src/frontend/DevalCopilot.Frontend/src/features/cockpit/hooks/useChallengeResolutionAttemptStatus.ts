import { useCallback, useEffect, useRef, useState } from 'react'
import type { ChallengeResolutionAttemptStatusResponse } from '../../../api/clients'
import { challengeResolutionAttemptStatusClient } from '../../../api/clients'

export interface UseChallengeResolutionAttemptStatusResult {
  status: ChallengeResolutionAttemptStatusResponse | null
  loading: boolean
  error: string | null
  refresh: () => void
}

/** The result actually committed so far, tagged with the runId it belongs to. Mirrors
 * `useClaudeCriticalReviewAttemptStatus`'s identically named type exactly. */
interface CommittedResult {
  runId: string | null
  status: ChallengeResolutionAttemptStatusResponse | null
  error: string | null
}

const EMPTY_RESULT: CommittedResult = { runId: null, status: null, error: null }

/**
 * Reads the most recent Codex challenge-resolution attempt for a run. `latestEventSequence`
 * re-triggers a fetch whenever any run event advances, and `refresh` lets a caller force one
 * immediately after successfully requesting a new attempt.
 *
 * Isolation across a run switch is enforced at render time, not by an effect — mirrors
 * `useClaudeCriticalReviewAttemptStatus`'s own doc comment and implementation exactly; see that
 * hook for the full rationale.
 */
export function useChallengeResolutionAttemptStatus(
  runId: string | null,
  latestEventSequence: number | undefined,
): UseChallengeResolutionAttemptStatusResult {
  const [committed, setCommitted] = useState<CommittedResult>(EMPTY_RESULT)
  const [loading, setLoading] = useState(true)
  const [refreshToken, setRefreshToken] = useState(0)
  const generationRef = useRef(0)

  const refresh = useCallback(() => {
    setRefreshToken((token) => token + 1)
  }, [])

  useEffect(() => {
    const generation = ++generationRef.current

    if (!runId) {
      setCommitted(EMPTY_RESULT)
      setLoading(false)
      return
    }

    setLoading(true)
    challengeResolutionAttemptStatusClient()
      .getChallengeResolutionAttemptStatus(runId)
      .then((response) => {
        if (generationRef.current === generation) {
          setCommitted({ runId, status: response.hasAttempt ? response : null, error: null })
        }
      })
      .catch(() => {
        if (generationRef.current === generation) {
          setCommitted({ runId, status: null, error: 'Challenge resolution attempt status is unavailable.' })
        }
      })
      .finally(() => {
        if (generationRef.current === generation) {
          setLoading(false)
        }
      })

    return () => {
      generationRef.current += 1
    }
  }, [runId, latestEventSequence, refreshToken])

  const belongsToCurrentRun = committed.runId === runId
  return {
    status: belongsToCurrentRun ? committed.status : null,
    error: belongsToCurrentRun ? committed.error : null,
    loading: belongsToCurrentRun ? loading : runId !== null,
    refresh,
  }
}
