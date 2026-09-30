import { useCallback } from 'react'
import { ApiException, SetCodexAssignmentPreferenceRequest } from '../../../api/generated/api-client'
import { setCodexAssignmentPreferenceClient } from '../../../api/clients'
import { useRunScopedAction } from './useRunScopedAction'

interface UseSetCodexAssignmentPreferenceResult {
  saving: boolean
  error: string | null
  save: (runId: string, requestedModel: string | null, requestedEffort: string | null) => Promise<boolean>
}

const GENERIC_MESSAGE = 'The Codex model/effort preference could not be saved for this run.'

/**
 * Extracts only the fixed, safe error text the backend itself wrote (an `ApiError.detail` value
 * from the structured problem-details body) — never a raw exception message, stack trace, or
 * anything else the transport layer might have captured.
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

/**
 * Sets or clears the run-scoped requested Codex model/effort for future Planner, Challenge
 * Resolver, and Code Reviewer claims, then triggers the caller's own cockpit refresh. This never
 * affects an already-claimed attempt's own immutable assignment.
 *
 * Bound to `currentRunId`'s interaction lifetime: an obsolete completion, a foreign `runId`, or a second
 * submission while one is in flight changes nothing and resolves false, so a caller never updates a
 * saved value or draft for work that no longer belongs to the current lifetime.
 */
export function useSetCodexAssignmentPreference(currentRunId: string, onSaved: () => void): UseSetCodexAssignmentPreferenceResult {
  const { busy, error, run } = useRunScopedAction(currentRunId)

  const save = useCallback(
    (runId: string, requestedModel: string | null, requestedEffort: string | null) =>
      run(
        runId,
        () =>
          setCodexAssignmentPreferenceClient().setCodexAssignmentPreference(
            runId,
            new SetCodexAssignmentPreferenceRequest({
            requestedModel: requestedModel ?? undefined,
            requestedEffort: requestedEffort ?? undefined,
          }),
          ),
        { toMessage: extractSafeErrorDetail, onSuccess: onSaved },
      ),
    [run, onSaved],
  )

  return { saving: busy, error, save }
}
