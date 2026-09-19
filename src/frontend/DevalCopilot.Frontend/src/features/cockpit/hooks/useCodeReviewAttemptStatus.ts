import { useCallback, useEffect, useRef, useState } from 'react'
import type { CodeReviewAttemptStatusResponse } from '../../../api/clients'
import { codeReviewAttemptStatusClient } from '../../../api/clients'

export interface UseCodeReviewAttemptStatusResult {
  status: CodeReviewAttemptStatusResponse | null
  loading: boolean
  error: string | null
  refresh: () => void
}

/** Mirrors `useClaudeCriticalReviewAttemptStatus`'s `CommittedResult` pattern exactly — kept as
 * one atomic value so a single `setState` call can never leave one field updated for the new run
 * while another still reflects the old one. */
interface CommittedResult {
  runId: string | null
  status: CodeReviewAttemptStatusResponse | null
  error: string | null
}

const EMPTY_RESULT: CommittedResult = { runId: null, status: null, error: null }

/**
 * Reads the most recent Codex code-review attempt for a run. `latestEventSequence` re-triggers a
 * fetch whenever any run event advances, and `refresh` lets a caller force one immediately after
 * successfully requesting a new attempt, since claiming an attempt does not itself emit a run
 * event. Mirrors `useClaudeCriticalReviewAttemptStatus` exactly, including its render-time
 * cross-run isolation.
 */
export function useCodeReviewAttemptStatus(
  runId: string | null,
  latestEventSequence: number | undefined,
): UseCodeReviewAttemptStatusResult {
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
    codeReviewAttemptStatusClient()
      .getCodeReviewAttemptStatus(runId)
      .then((response) => {
        if (generationRef.current === generation) {
          setCommitted({ runId, status: response.hasAttempt ? response : null, error: null })
        }
      })
      .catch(() => {
        if (generationRef.current === generation) {
          setCommitted({ runId, status: null, error: 'Code review attempt status is unavailable.' })
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
