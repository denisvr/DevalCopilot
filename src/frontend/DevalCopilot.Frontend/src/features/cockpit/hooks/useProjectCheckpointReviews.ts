import { useCallback, useEffect, useState } from 'react'
import {
  projectCheckpointReviewsClient,
  recordCheckpointReviewClient,
  RecordCheckpointReviewRequest,
} from '../../../api/clients'
import type { CheckpointReviewResponse } from '../../../api/clients'

export function useProjectCheckpointReviews(projectId: string | null) {
  const [reviews, setReviews] = useState<CheckpointReviewResponse[]>([])
  const [error, setError] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)

  const refresh = useCallback(async () => {
    if (!projectId) {
      setReviews([])
      return []
    }

    try {
      const next = await projectCheckpointReviewsClient().getProjectCheckpointReviews(projectId)
      setReviews(next)
      setError(null)
      return next
    } catch {
      setError('Review evidence could not be loaded.')
      return []
    }
  }, [projectId])

  useEffect(() => {
    queueMicrotask(() => void refresh())
  }, [refresh])

  const record = useCallback(async (checkpointId: string, executionId: string | undefined, decision: string, actorKind = 'Human') => {
    if (!projectId) {
      return false
    }

    setSaving(true)
    setError(null)
    try {
      await recordCheckpointReviewClient().recordCheckpointReview(
        projectId,
        new RecordCheckpointReviewRequest({
          gitCheckpointId: checkpointId,
          verificationExecutionId: executionId,
          actorKind,
          decision,
        }),
      )
      await refresh()
      return true
    } catch {
      setError('This review decision could not be recorded.')
      return false
    } finally {
      setSaving(false)
    }
  }, [projectId, refresh])

  return { reviews, error, saving, refresh, record }
}
