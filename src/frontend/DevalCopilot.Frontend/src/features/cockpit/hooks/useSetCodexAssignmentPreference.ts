import { useCallback, useState } from 'react'
import { ApiException, SetCodexAssignmentPreferenceRequest } from '../../../api/generated/api-client'
import { setCodexAssignmentPreferenceClient } from '../../../api/clients'

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
 */
export function useSetCodexAssignmentPreference(onSaved: () => void): UseSetCodexAssignmentPreferenceResult {
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const save = useCallback(
    async (runId: string, requestedModel: string | null, requestedEffort: string | null) => {
      setSaving(true)
      setError(null)
      try {
        await setCodexAssignmentPreferenceClient().setCodexAssignmentPreference(
          runId,
          new SetCodexAssignmentPreferenceRequest({
            requestedModel: requestedModel ?? undefined,
            requestedEffort: requestedEffort ?? undefined,
          }),
        )
        onSaved()
        return true
      } catch (caught: unknown) {
        setError(extractSafeErrorDetail(caught))
        return false
      } finally {
        setSaving(false)
      }
    },
    [onSaved],
  )

  return { saving, error, save }
}
