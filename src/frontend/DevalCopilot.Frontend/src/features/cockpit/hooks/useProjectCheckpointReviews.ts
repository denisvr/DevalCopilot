import { useCallback, useEffect } from 'react'
import {
  projectCheckpointReviewsClient,
  recordCheckpointReviewClient,
  RecordCheckpointReviewRequest,
} from '../../../api/clients'
import type { CheckpointReviewResponse } from '../../../api/clients'
import { useOwnedLifetime, useOwnedState } from './useOwnedLifetime'

interface ReviewsFrame {
  reviews: CheckpointReviewResponse[]
  error: string | null
  saving: boolean
}

function createFrame(): ReviewsFrame {
  return { reviews: [], error: null, saving: false }
}

/** Review evidence and the pending decision belong to the current project's lifetime. A decision
 * the server already accepted stays real when the project changes meanwhile, but its continuation
 * then neither refreshes, reports, nor resolves true for the replacement. */
export function useProjectCheckpointReviews(projectId: string | null) {
  const owner = useOwnedLifetime(projectId)
  const [frame, commit] = useOwnedState(owner, createFrame)

  const refresh = useCallback(async () => {
    if (!projectId || !owner.isActive()) {
      return []
    }

    const isCurrent = owner.begin('read')
    try {
      const next = await projectCheckpointReviewsClient().getProjectCheckpointReviews(projectId)
      if (isCurrent()) {
        commit((previous) => ({ ...previous, reviews: next, error: null }))
      }
      return next
    } catch {
      if (isCurrent()) {
        commit((previous) => ({ ...previous, error: 'Review evidence could not be loaded.' }))
      }
      return []
    }
  }, [owner, projectId, commit])

  useEffect(() => {
    queueMicrotask(() => void refresh())
  }, [refresh])

  const record = useCallback(async (checkpointId: string, executionId: string | undefined, decision: string, actorKind = 'Human') => {
    if (!projectId || !owner.isActive()) {
      return false
    }

    const isCurrent = owner.begin('record')
    commit((previous) => ({ ...previous, saving: true, error: null }))
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
      if (!isCurrent()) {
        return false
      }
      await refresh()
      return isCurrent()
    } catch {
      if (isCurrent()) {
        commit((previous) => ({ ...previous, error: 'This review decision could not be recorded.' }))
      }
      return false
    } finally {
      if (isCurrent()) {
        commit((previous) => ({ ...previous, saving: false }))
      }
    }
  }, [owner, projectId, commit, refresh])

  return { ...frame, refresh, record }
}
