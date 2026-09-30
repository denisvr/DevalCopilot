import { useCallback } from 'react'
import { ApiException, RequestClaudeCriticalReviewRequest } from '../../../api/generated/api-client'
import { requestClaudeCriticalReviewClient } from '../../../api/clients'
import { useRunScopedAction } from './useRunScopedAction'

interface UseRequestClaudeCriticalReviewResult {
  requesting: boolean
  error: string | null
  request: (runId: string, proposalMessageId: string) => Promise<boolean>
}

const GENERIC_MESSAGE = 'A Claude critical review could not be requested for this run.'

/**
 * Extracts only the fixed, safe error text the backend itself wrote (an `ApiError.detail`
 * value from the structured problem-details body) — never a raw exception message, stack
 * trace, or anything else the transport layer might have captured.
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

/** Requests one durable Claude critical-review attempt of a specific Proposal message, then
 * triggers the caller's own status refresh. */
/** Requests one durable Claude critical-review attempt of a specific Proposal message, then
 * triggers the caller's own status refresh.
 * The request is bound to `currentRunId`'s interaction lifetime: an obsolete completion, a
 * foreign `runId`, or a duplicate of an in-flight submission never changes the current state. */
export function useRequestClaudeCriticalReview(currentRunId: string, onRequested: () => void): UseRequestClaudeCriticalReviewResult {
  const { busy, error, run } = useRunScopedAction(currentRunId)

  const request = useCallback(
    (runId: string, proposalMessageId: string) =>
      run(
        runId,
        () =>
          requestClaudeCriticalReviewClient().requestClaudeCriticalReview(
            runId,
            new RequestClaudeCriticalReviewRequest({ proposalMessageId }),
          ),
        { toMessage: extractSafeErrorDetail, onSuccess: onRequested },
      ),
    [run, onRequested],
  )

  return { requesting: busy, error, request }
}
