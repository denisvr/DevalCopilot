import { useCallback, useEffect, useLayoutEffect, useRef } from 'react'
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
  // The refresh generation the newest accepted successful read belongs to, whether the newest accepted read failed, and
  // whether a read is in flight.
  readGeneration: number | null
  readFailed: boolean
  loading: boolean
}

function createFrame(): ReviewsFrame {
  return { reviews: [], error: null, saving: false, readGeneration: null, readFailed: false, loading: false }
}

/** Review evidence and the pending decision belong to the current project's lifetime. A decision
 * the server already accepted stays real when the project changes meanwhile, but its continuation
 * then neither refreshes, reports, nor resolves true for the replacement.
 *
 * The project's owner may ask for a fresh read by advancing `refreshGeneration`. The review list, including each review's
 * applicability to the current checkpoint, is `current` only while no read is in flight (the read that follows an accepted decision
 * included) and the newest accepted read, begun for the present generation, succeeded; the cached list stays visible as history.
 * A decision the server accepted is never reported as failed because the read that follows it failed. */
export function useProjectCheckpointReviews(projectId: string | null, refreshGeneration = 0) {
  const owner = useOwnedLifetime(projectId)
  const [frame, commit] = useOwnedState(owner, createFrame)

  // A read belongs to the generation that is current when it begins (see the executions hook).
  const generation = useRef(refreshGeneration)
  useLayoutEffect(() => {
    generation.current = refreshGeneration
  }, [refreshGeneration])

  const refresh = useCallback(async () => {
    if (!projectId || !owner.isActive()) {
      return []
    }

    const isCurrent = owner.begin('read')
    const readGeneration = generation.current
    commit((previous) => ({ ...previous, loading: true }))
    try {
      const next = await projectCheckpointReviewsClient().getProjectCheckpointReviews(projectId)
      if (isCurrent()) {
        commit((previous) => ({ ...previous, reviews: next, error: null, readGeneration, readFailed: false }))
      }
      return next
    } catch {
      if (isCurrent()) {
        commit((previous) => ({ ...previous, error: 'Review evidence could not be loaded.', readFailed: true }))
      }
      return []
    } finally {
      if (isCurrent()) {
        commit((previous) => ({ ...previous, loading: false }))
      }
    }
  }, [owner, projectId, commit])

  useEffect(() => {
    queueMicrotask(() => void refresh())
  }, [refresh, refreshGeneration])

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

  return { ...frame, current: frame.readGeneration === refreshGeneration && !frame.readFailed && !frame.loading, refresh, record }
}
