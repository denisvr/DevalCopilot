import { useCallback } from 'react'
import { ApiException } from '../../../api/generated/api-client'
import { requestCodexPlanningAttemptClient } from '../../../api/clients'
import { useRunScopedAction } from './useRunScopedAction'

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
export function extractSafeErrorDetail(caught: unknown, fallback: string = GENERIC_MESSAGE): string {
  if (!ApiException.isApiException(caught)) {
    return fallback
  }

  try {
    const parsed = JSON.parse((caught as ApiException).response) as { errors?: { detail?: string }[] }
    const detail = parsed.errors?.[0]?.detail
    return typeof detail === 'string' && detail.length > 0 ? detail : fallback
  } catch {
    return fallback
  }
}

/** Requests one durable Codex planning attempt, then triggers the caller's own status refresh. */
/** Requests one durable Codex planning attempt, then triggers the caller's own status refresh.
 * The request is bound to `currentRunId`'s interaction lifetime: an obsolete completion, a
 * foreign `runId`, or a duplicate of an in-flight submission never changes the current state. */
export function useRequestCodexPlanningAttempt(currentRunId: string, onRequested: () => void): UseRequestCodexPlanningAttemptResult {
  const { busy, error, run } = useRunScopedAction(currentRunId)

  const request = useCallback(
    (runId: string) =>
      run(
        runId,
        () =>
          requestCodexPlanningAttemptClient().requestCodexPlanningAttempt(
            runId
          ),
        { toMessage: extractSafeErrorDetail, onSuccess: onRequested },
      ),
    [run, onRequested],
  )

  return { requesting: busy, error, request }
}
