import { useCallback } from 'react'
import { ApiException, RequestReviewCorrectionRequest } from '../../../api/generated/api-client'
import { requestReviewCorrectionClient } from '../../../api/clients'
import { describeDirectGuidanceFailure } from './directGuidanceFailure'
import { useRunScopedAction } from './useRunScopedAction'

interface UseRequestReviewCorrectionResult {
  requesting: boolean
  error: string | null
  /** `guidance` is undefined for the plain request; otherwise the raw draft, sent together with the
   * review attempt. The server normalizes, bounds, and decides eligibility (including exhaustion). */
  request: (runId: string, implementationReviewAttemptId: string, guidance?: string) => Promise<boolean>
}

const GENERIC_MESSAGE = 'A review correction could not be requested for this run.'

/** The run and the review attempt together own one request's lifetime; replacing either ends the old one. */
const ownerOf = (runId: string, implementationReviewAttemptId: string | null) =>
  JSON.stringify([runId, implementationReviewAttemptId])

function extractSafeErrorDetail(caught: unknown): string {
  const guidance = describeDirectGuidanceFailure(caught)
  if (guidance) return guidance
  if (!ApiException.isApiException(caught)) return GENERIC_MESSAGE
  try {
    const parsed = JSON.parse((caught as ApiException).response) as { errors?: { detail?: string }[] }
    const detail = parsed.errors?.[0]?.detail
    return typeof detail === 'string' && detail.length > 0 ? detail : GENERIC_MESSAGE
  } catch {
    return GENERIC_MESSAGE
  }
}

/** Requests one durable Claude review-correction attempt of a specific code-review attempt,
 * optionally with direct human guidance, then triggers the caller's own status refresh.
 * The request is bound to the interaction lifetime of `currentRunId` AND `currentImplementationReviewAttemptId`:
 * an obsolete completion, a foreign run or review, or a duplicate of an in-flight submission never
 * changes the current state. An accepted obsolete request stays a real server operation. */
export function useRequestReviewCorrection(
  currentRunId: string,
  currentImplementationReviewAttemptId: string | null,
  onRequested: () => void,
): UseRequestReviewCorrectionResult {
  const { busy, error, run } = useRunScopedAction(ownerOf(currentRunId, currentImplementationReviewAttemptId))

  const request = useCallback(
    (runId: string, implementationReviewAttemptId: string, guidance?: string) =>
      run(
        ownerOf(runId, implementationReviewAttemptId),
        () =>
          requestReviewCorrectionClient().requestReviewCorrection(
            runId,
            new RequestReviewCorrectionRequest({ implementationReviewAttemptId, guidance }),
          ),
        { toMessage: extractSafeErrorDetail, onSuccess: onRequested },
      ),
    [run, onRequested],
  )

  return { requesting: busy, error, request }
}
