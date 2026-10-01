import { useCallback } from 'react'
import { ApiException, RequestImplementationRequest } from '../../../api/generated/api-client'
import { requestImplementationClient } from '../../../api/clients'
import { describeDirectGuidanceFailure } from './directGuidanceFailure'
import { useRunScopedAction } from './useRunScopedAction'

interface UseRequestImplementationResult {
  requesting: boolean
  error: string | null
  /** `guidance` is undefined for the plain request; otherwise the raw draft, sent together with the
   * plan. The server normalizes, bounds, and decides eligibility. */
  request: (runId: string, planProposalMessageId: string, guidance?: string) => Promise<boolean>
}

const GENERIC_MESSAGE = 'An implementation could not be requested for this run.'

/** The run and the plan together own one request's lifetime; replacing either ends the old one. */
const ownerOf = (runId: string, planProposalMessageId: string | null) => JSON.stringify([runId, planProposalMessageId])

/**
 * Extracts only the fixed, safe error text the backend itself wrote (an `ApiError.detail`
 * value from the structured problem-details body) — never a raw exception message, stack
 * trace, or anything else the transport layer might have captured. The two direct-guidance
 * refusals map to fixed local text instead. Mirrors `useRequestChallengeResolution`'s
 * identically named helper otherwise.
 */
function extractSafeErrorDetail(caught: unknown): string {
  const guidance = describeDirectGuidanceFailure(caught)
  if (guidance) {
    return guidance
  }
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
 * plan, optionally with direct human guidance, then triggers the caller's own status refresh.
 * The request is bound to the interaction lifetime of `currentRunId` AND `currentPlanProposalMessageId`:
 * an obsolete completion, a foreign run or plan, or a duplicate of an in-flight submission never
 * changes the current state. An accepted obsolete request stays a real server operation. */
export function useRequestImplementation(
  currentRunId: string,
  currentPlanProposalMessageId: string | null,
  onRequested: () => void,
): UseRequestImplementationResult {
  const { busy, error, run } = useRunScopedAction(ownerOf(currentRunId, currentPlanProposalMessageId))

  const request = useCallback(
    (runId: string, planProposalMessageId: string, guidance?: string) =>
      run(
        ownerOf(runId, planProposalMessageId),
        () =>
          requestImplementationClient().requestImplementation(
            runId,
            new RequestImplementationRequest({ planProposalMessageId, guidance }),
          ),
        { toMessage: extractSafeErrorDetail, onSuccess: onRequested },
      ),
    [run, onRequested],
  )

  return { requesting: busy, error, request }
}
