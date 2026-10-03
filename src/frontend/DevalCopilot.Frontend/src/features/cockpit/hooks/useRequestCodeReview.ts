import { useCallback } from 'react'
import { ApiException, RequestCodeReviewRequest } from '../../../api/generated/api-client'
import { requestCodeReviewClient } from '../../../api/clients'
import { useRunScopedAction } from './useRunScopedAction'

interface UseRequestCodeReviewResult {
  requesting: boolean
  error: string | null
  request: (runId: string, executionReportMessageId: string) => Promise<boolean>
}

const GENERIC_MESSAGE = 'A code review could not be requested for this run.'

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

/** Requests one durable Codex code-review attempt of a specific implementation ExecutionReport
 * message, then triggers the caller's own status refresh. Mirrors
 * `useRequestClaudeCriticalReview`/`useRequestChallengeResolution` exactly. */
/** Requests one durable Codex code-review attempt of a specific implementation ExecutionReport
 * message, then triggers the caller's own status refresh.
 * The request is bound to `currentRunId`'s interaction lifetime: an obsolete completion, a
 * foreign `runId`, or a duplicate of an in-flight submission never changes the current state.
 * A refusal reported before the latest evidence refresh (`evidenceRefreshGeneration`) is no longer shown. */
export function useRequestCodeReview(
  currentRunId: string,
  onRequested: () => void,
  evidenceRefreshGeneration = 0,
): UseRequestCodeReviewResult {
  const { busy, error, run } = useRunScopedAction(currentRunId, evidenceRefreshGeneration)

  const request = useCallback(
    (runId: string, executionReportMessageId: string) =>
      run(
        runId,
        () =>
          requestCodeReviewClient().requestCodeReview(
            runId,
            new RequestCodeReviewRequest({ executionReportMessageId }),
          ),
        { toMessage: extractSafeErrorDetail, onSuccess: onRequested },
      ),
    [run, onRequested],
  )

  return { requesting: busy, error, request }
}
