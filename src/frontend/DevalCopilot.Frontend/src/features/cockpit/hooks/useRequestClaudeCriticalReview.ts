import { useCallback, useState } from 'react'
import { ApiException, RequestClaudeCriticalReviewRequest } from '../../../api/generated/api-client'
import { requestClaudeCriticalReviewClient } from '../../../api/clients'

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
export function useRequestClaudeCriticalReview(onRequested: () => void): UseRequestClaudeCriticalReviewResult {
  const [requesting, setRequesting] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const request = useCallback(
    async (runId: string, proposalMessageId: string) => {
      setRequesting(true)
      setError(null)
      try {
        await requestClaudeCriticalReviewClient().requestClaudeCriticalReview(
          runId,
          new RequestClaudeCriticalReviewRequest({ proposalMessageId }),
        )
        onRequested()
        return true
      } catch (caught: unknown) {
        setError(extractSafeErrorDetail(caught))
        return false
      } finally {
        setRequesting(false)
      }
    },
    [onRequested],
  )

  return { requesting, error, request }
}
