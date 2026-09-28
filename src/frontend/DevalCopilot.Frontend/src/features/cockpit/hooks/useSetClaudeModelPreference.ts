import { useCallback, useState } from 'react'
import { ApiException, SetClaudeModelPreferenceRequest } from '../../../api/generated/api-client'
import { setClaudeModelPreferenceClient } from '../../../api/clients'

interface UseSetClaudeModelPreferenceResult {
  saving: boolean
  error: string | null
  save: (runId: string, requestedModel: string | null) => Promise<boolean>
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
 * Sets or clears the run-scoped requested Claude model alias for future CriticalReviewer,
 * Implementer, and ReviewCorrection claims. This never affects an already-claimed attempt's own
 * immutable request, and the alias is a request only — never an observed or effective model.
 */
export function useSetClaudeModelPreference(): UseSetClaudeModelPreferenceResult {
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const save = useCallback(async (runId: string, requestedModel: string | null) => {
    setSaving(true)
    setError(null)
    try {
      await setClaudeModelPreferenceClient().setClaudeModelPreference(
        runId,
        new SetClaudeModelPreferenceRequest({ requestedModel: requestedModel ?? undefined }),
      )
      return true
    } catch (caught: unknown) {
      setError(extractSafeErrorDetail(caught))
      return false
    } finally {
      setSaving(false)
    }
  }, [])

  return { saving, error, save }
}
