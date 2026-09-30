import { useCallback } from 'react'
import { ApiException, AuthorizeReviewCorrectionWithGuidanceRequest } from '../../../api/generated/api-client'
import { authorizeReviewCorrectionClient, authorizeReviewCorrectionWithGuidanceClient } from '../../../api/clients'
import { useRunScopedAction } from './useRunScopedAction'

interface UseAuthorizeReviewCorrectionResult {
  authorizing: boolean
  error: string | null
  /** `guidance` is undefined for the bodyless authorization; otherwise the raw draft, sent to the
   * guided operation. The server normalizes, bounds, and decides eligibility. */
  authorize: (runId: string, escalationId: string, guidance?: string) => Promise<boolean>
}

const GENERIC_MESSAGE = 'An additional correction could not be authorized for this run.'

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

/**
 * Authorizes one additional review correction (plain or with guidance). Bound to `currentRunId`'s
 * interaction lifetime: an obsolete completion, a foreign `runId`, or a second submission while
 * one is in flight changes nothing, and resolves false so a caller never clears a draft or reports
 * success for work that no longer belongs to the current lifetime.
 */
export function useAuthorizeReviewCorrection(currentRunId: string, onAuthorized: () => void): UseAuthorizeReviewCorrectionResult {
  const { busy, error, run } = useRunScopedAction(currentRunId)

  const authorize = useCallback(
    (runId: string, escalationId: string, guidance?: string) =>
      run(
        runId,
        () =>
          guidance === undefined
            ? authorizeReviewCorrectionClient().authorizeReviewCorrection(runId, escalationId)
            : authorizeReviewCorrectionWithGuidanceClient().authorizeReviewCorrectionWithGuidance(
                runId,
                escalationId,
                new AuthorizeReviewCorrectionWithGuidanceRequest({ guidance }),
              ),
        { toMessage: extractSafeErrorDetail, onSuccess: onAuthorized },
      ),
    [run, onAuthorized],
  )

  return { authorizing: busy, error, authorize }
}
