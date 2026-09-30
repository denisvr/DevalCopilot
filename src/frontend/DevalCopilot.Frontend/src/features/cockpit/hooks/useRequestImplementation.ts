import { useCallback } from 'react'
import { ApiException, RequestImplementationRequest } from '../../../api/generated/api-client'
import { requestImplementationClient } from '../../../api/clients'
import { useRunScopedAction } from './useRunScopedAction'

interface UseRequestImplementationResult {
  requesting: boolean
  error: string | null
  request: (runId: string, planProposalMessageId: string) => Promise<boolean>
}

const GENERIC_MESSAGE = 'An implementation could not be requested for this run.'

/**
 * Extracts only the fixed, safe error text the backend itself wrote (an `ApiError.detail`
 * value from the structured problem-details body) — never a raw exception message, stack
 * trace, or anything else the transport layer might have captured. Mirrors
 * `useRequestChallengeResolution`'s identically named helper exactly.
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

/** Requests one durable Claude implementation attempt of a specific authoritative resolved
 * plan, then triggers the caller's own status refresh. Mirrors `useRequestChallengeResolution`
 * exactly. */
/** Requests one durable Claude implementation attempt of a specific authoritative resolved
 * plan, then triggers the caller's own status refresh.
 * The request is bound to `currentRunId`'s interaction lifetime: an obsolete completion, a
 * foreign `runId`, or a duplicate of an in-flight submission never changes the current state. */
export function useRequestImplementation(currentRunId: string, onRequested: () => void): UseRequestImplementationResult {
  const { busy, error, run } = useRunScopedAction(currentRunId)

  const request = useCallback(
    (runId: string, planProposalMessageId: string) =>
      run(
        runId,
        () =>
          requestImplementationClient().requestImplementation(
            runId,
            new RequestImplementationRequest({ planProposalMessageId }),
          ),
        { toMessage: extractSafeErrorDetail, onSuccess: onRequested },
      ),
    [run, onRequested],
  )

  return { requesting: busy, error, request }
}
