import { useCallback } from 'react'
import { ApiException, RequestReviewCorrectionRequest } from '../../../api/generated/api-client'
import { requestReviewCorrectionClient } from '../../../api/clients'
import { useRunScopedAction } from './useRunScopedAction'

interface UseRequestReviewCorrectionResult {
  requesting: boolean
  error: string | null
  request: (runId: string, implementationReviewAttemptId: string) => Promise<boolean>
}

const GENERIC_MESSAGE = 'A review correction could not be requested for this run.'

function extractSafeErrorDetail(caught: unknown): string {
  if (!ApiException.isApiException(caught)) return GENERIC_MESSAGE
  try {
    const parsed = JSON.parse((caught as ApiException).response) as { errors?: { detail?: string }[] }
    const detail = parsed.errors?.[0]?.detail
    return typeof detail === 'string' && detail.length > 0 ? detail : GENERIC_MESSAGE
  } catch {
    return GENERIC_MESSAGE
  }
}

/** Requests one durable Claude review-correction attempt of a specific code-review attempt, then
 * triggers the caller's own status refresh.
 * The request is bound to `currentRunId`'s interaction lifetime: an obsolete completion, a
 * foreign `runId`, or a duplicate of an in-flight submission never changes the current state. */
export function useRequestReviewCorrection(currentRunId: string, onRequested: () => void): UseRequestReviewCorrectionResult {
  const { busy, error, run } = useRunScopedAction(currentRunId)

  const request = useCallback(
    (runId: string, implementationReviewAttemptId: string) =>
      run(
        runId,
        () =>
          requestReviewCorrectionClient().requestReviewCorrection(
            runId,
            new RequestReviewCorrectionRequest({ implementationReviewAttemptId }),
          ),
        { toMessage: extractSafeErrorDetail, onSuccess: onRequested },
      ),
    [run, onRequested],
  )

  return { requesting: busy, error, request }
}
