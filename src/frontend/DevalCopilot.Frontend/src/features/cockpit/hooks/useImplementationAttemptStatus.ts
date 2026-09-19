import { useCallback, useEffect, useRef, useState } from 'react'
import type { ImplementationAttemptStatusResponse } from '../../../api/clients'
import { implementationAttemptStatusClient } from '../../../api/clients'

export interface UseImplementationAttemptStatusResult {
  status: ImplementationAttemptStatusResponse | null
  loading: boolean
  error: string | null
  refresh: () => void
}

/** The result actually committed so far, tagged with the runId it belongs to. Mirrors
 * `useChallengeResolutionAttemptStatus`'s identically named type exactly. */
interface CommittedResult {
  runId: string | null
  status: ImplementationAttemptStatusResponse | null
  error: string | null
}

const EMPTY_RESULT: CommittedResult = { runId: null, status: null, error: null }

/**
 * Reads the most recent Claude implementation attempt for a run. `latestEventSequence`
 * re-triggers a fetch whenever any run event advances, and `refresh` lets a caller force one
 * immediately after successfully requesting a new attempt.
 *
 * Isolation across a run switch is enforced at render time, not by an effect — mirrors
 * `useChallengeResolutionAttemptStatus`'s own doc comment and implementation exactly; see that
 * hook for the full rationale.
 */
export function useImplementationAttemptStatus(
  runId: string | null,
  latestEventSequence: number | undefined,
): UseImplementationAttemptStatusResult {
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
    implementationAttemptStatusClient()
      .getImplementationAttemptStatus(runId)
      .then((response) => {
        if (generationRef.current === generation) {
          setCommitted({ runId, status: response.hasAttempt ? response : null, error: null })
        }
      })
      .catch(() => {
        if (generationRef.current === generation) {
          setCommitted({ runId, status: null, error: 'Implementation attempt status is unavailable.' })
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
