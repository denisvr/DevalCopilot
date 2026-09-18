import { useCallback, useState } from 'react'
import { ApiException } from '../../../api/generated/api-client'
import { requestCodexPlanningAttemptClient } from '../../../api/clients'

interface UseRequestCodexPlanningAttemptResult {
  requesting: boolean
  error: string | null
  request: (runId: string) => Promise<boolean>
}

const GENERIC_MESSAGE = 'A Codex plan could not be requested for this run.'

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

/** Requests one durable Codex planning attempt, then triggers the caller's own status refresh. */
export function useRequestCodexPlanningAttempt(onRequested: () => void): UseRequestCodexPlanningAttemptResult {
  const [requesting, setRequesting] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const request = useCallback(
    async (runId: string) => {
      setRequesting(true)
      setError(null)
      try {
        await requestCodexPlanningAttemptClient().requestCodexPlanningAttempt(runId)
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
