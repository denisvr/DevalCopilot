import { useCallback, useEffect, useRef, useState } from 'react'
import type { ReviewCorrectionAttemptStatusResponse } from '../../../api/clients'
import { reviewCorrectionAttemptStatusClient } from '../../../api/clients'

export interface UseReviewCorrectionAttemptStatusResult {
  status: ReviewCorrectionAttemptStatusResponse | null
  loading: boolean
  error: string | null
  refresh: () => void
}

interface CommittedResult {
  runId: string | null
  status: ReviewCorrectionAttemptStatusResponse | null
  error: string | null
}

const EMPTY_RESULT: CommittedResult = { runId: null, status: null, error: null }

export function useReviewCorrectionAttemptStatus(
  runId: string | null,
  latestEventSequence: number | undefined,
): UseReviewCorrectionAttemptStatusResult {
  const [committed, setCommitted] = useState<CommittedResult>(EMPTY_RESULT)
  const [loading, setLoading] = useState(true)
  const [refreshToken, setRefreshToken] = useState(0)
  const generationRef = useRef(0)

  const refresh = useCallback(() => setRefreshToken((token) => token + 1), [])

  useEffect(() => {
    const generation = ++generationRef.current
    if (!runId) {
      setCommitted(EMPTY_RESULT)
      setLoading(false)
      return
    }

    setLoading(true)
    reviewCorrectionAttemptStatusClient()
      .getReviewCorrectionAttemptStatus(runId)
      .then((response) => {
        if (generationRef.current === generation) {
          setCommitted({ runId, status: response.hasAttempt ? response : null, error: null })
        }
      })
      .catch(() => {
        if (generationRef.current === generation) {
          setCommitted((current) => ({
            runId,
            status: current.runId === runId ? current.status : null,
            error: 'Review correction status is unavailable.',
          }))
        }
      })
      .finally(() => {
        if (generationRef.current === generation) setLoading(false)
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
