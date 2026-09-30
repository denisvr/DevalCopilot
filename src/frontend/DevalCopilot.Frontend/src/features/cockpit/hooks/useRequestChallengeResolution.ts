import { useCallback } from 'react'
import { ApiException, RequestChallengeResolutionRequest } from '../../../api/generated/api-client'
import { requestChallengeResolutionClient } from '../../../api/clients'
import { useRunScopedAction } from './useRunScopedAction'

interface UseRequestChallengeResolutionResult {
  requesting: boolean
  error: string | null
  request: (runId: string, challengedReviewAttemptId: string) => Promise<boolean>
}

const GENERIC_MESSAGE = 'A challenge resolution could not be requested for this run.'

/**
 * Extracts only the fixed, safe error text the backend itself wrote (an `ApiError.detail`
 * value from the structured problem-details body) — never a raw exception message, stack
 * trace, or anything else the transport layer might have captured. Mirrors
 * `useRequestClaudeCriticalReview`'s identically named helper exactly.
 */
function extractSafeErrorDetail(caught: unknown): string {
  if (!ApiException.isApiException(caught)) {
    return GENERIC_MESSAGE
  }

  try {
    const parsed = JSON.parse((caught as ApiException).response) as { errors?: { detail?: string }[] }
    const detail = parsed.errors?.[0]?.detail
    return typeof detail === 'string' && detail.length > 0 ? detail : GENERIC_MESSAGE
  } catch {
    return GENERIC_MESSAGE
  }
}

/** Requests one durable Codex challenge-resolution attempt of a specific Challenged Claude
 * critical-review attempt, then triggers the caller's own status refresh. Mirrors
 * `useRequestClaudeCriticalReview` exactly. */
/** Requests one durable Codex challenge-resolution attempt of a specific Challenged Claude
 * critical-review attempt, then triggers the caller's own status refresh.
 * The request is bound to `currentRunId`'s interaction lifetime: an obsolete completion, a
 * foreign `runId`, or a duplicate of an in-flight submission never changes the current state. */
export function useRequestChallengeResolution(currentRunId: string, onRequested: () => void): UseRequestChallengeResolutionResult {
  const { busy, error, run } = useRunScopedAction(currentRunId)

  const request = useCallback(
    (runId: string, challengedReviewAttemptId: string) =>
      run(
        runId,
        () =>
          requestChallengeResolutionClient().requestChallengeResolution(
            runId,
            new RequestChallengeResolutionRequest({ challengedReviewAttemptId }),
          ),
        { toMessage: extractSafeErrorDetail, onSuccess: onRequested },
      ),
    [run, onRequested],
  )

  return { requesting: busy, error, request }
}
