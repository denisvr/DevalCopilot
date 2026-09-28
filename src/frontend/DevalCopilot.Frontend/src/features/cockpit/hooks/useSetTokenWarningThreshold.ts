import { useCallback, useState } from 'react'
import { ApiException, SetTokenWarningThresholdRequest } from '../../../api/generated/api-client'
import { setTokenWarningThresholdClient } from '../../../api/clients'

interface UseSetTokenWarningThresholdResult {
  saving: boolean
  error: string | null
  save: (runId: string, provider: 'Codex' | 'ClaudeCode', thresholdTokens: number | null) => Promise<boolean>
}

const GENERIC_MESSAGE = 'The token warning threshold could not be saved for this run.'

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
 * Sets or clears one provider's advisory token-activity warning threshold for a run. The
 * threshold is a warning on locally recorded usage only — never a budget or an eligibility rule.
 */
export function useSetTokenWarningThreshold(): UseSetTokenWarningThresholdResult {
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const save = useCallback(async (runId: string, provider: 'Codex' | 'ClaudeCode', thresholdTokens: number | null) => {
    setSaving(true)
    setError(null)
    try {
      await setTokenWarningThresholdClient().setTokenWarningThreshold(
        runId,
        new SetTokenWarningThresholdRequest({ provider, thresholdTokens: thresholdTokens ?? undefined }),
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
