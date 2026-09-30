import { useCallback } from 'react'
import { ApiException, SetClaudeModelPreferenceRequest } from '../../../api/generated/api-client'
import { setClaudeModelPreferenceClient } from '../../../api/clients'
import { useRunScopedAction } from './useRunScopedAction'

interface UseSetClaudeModelPreferenceResult {
  saving: boolean
  error: string | null
  save: (runId: string, requestedModel: string | null, requestedEffort?: string | null) => Promise<boolean>
}

const GENERIC_MESSAGE = 'The Claude model request could not be saved for this run.'

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
 * Sets or clears the run-scoped requested Claude model alias and optional effort level for future CriticalReviewer,
 * Implementer, and ReviewCorrection claims. This never affects an already-claimed attempt's own
 * immutable request, and the pair is a request only — never an observed or effective model or effort.
 *
 * Bound to `currentRunId`'s interaction lifetime: an obsolete completion, a foreign `runId`, or a second
 * submission while one is in flight changes nothing and resolves false, so a caller never updates a
 * saved value or draft for work that no longer belongs to the current lifetime.
 */
export function useSetClaudeModelPreference(currentRunId: string): UseSetClaudeModelPreferenceResult {
  const { busy, error, run } = useRunScopedAction(currentRunId)

  const save = useCallback(
    (runId: string, requestedModel: string | null, requestedEffort: string | null = null) =>
      run(
        runId,
        () =>
          setClaudeModelPreferenceClient().setClaudeModelPreference(
            runId,
            new SetClaudeModelPreferenceRequest({
            requestedModel: requestedModel ?? undefined,
            requestedEffort: requestedEffort ?? undefined,
          }),
          ),
        { toMessage: extractSafeErrorDetail },
      ),
    [run],
  )

  return { saving: busy, error, save }
}
