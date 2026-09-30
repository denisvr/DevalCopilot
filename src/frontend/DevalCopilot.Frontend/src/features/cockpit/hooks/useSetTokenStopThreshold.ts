import { useCallback } from 'react'
import { ApiException, SetTokenStopThresholdRequest } from '../../../api/generated/api-client'
import { setTokenStopThresholdClient } from '../../../api/clients'
import { useRunScopedAction } from './useRunScopedAction'

interface UseSetTokenStopThresholdResult {
  saving: boolean
  error: string | null
  save: (runId: string, provider: 'Codex' | 'ClaudeCode', thresholdTokens: number | null) => Promise<boolean>
}

const GENERIC_MESSAGE = 'The token stop threshold could not be saved for this run.'

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
 * Sets or clears one provider's token-activity stop threshold for a run. The stop refuses a new
 * Agent claim for that provider once locally recorded usage reaches it; it is not an account
 * allowance, a per-attempt cap, or a reservation, and it never affects an already claimed attempt.
 *
 * Bound to `currentRunId`'s interaction lifetime: an obsolete completion, a foreign `runId`, or a second
 * submission while one is in flight changes nothing and resolves false, so a caller never updates a
 * saved value or draft for work that no longer belongs to the current lifetime.
 */
export function useSetTokenStopThreshold(currentRunId: string): UseSetTokenStopThresholdResult {
  const { busy, error, run } = useRunScopedAction(currentRunId)

  const save = useCallback(
    (runId: string, provider: 'Codex' | 'ClaudeCode', thresholdTokens: number | null) =>
      run(
        runId,
        () =>
          setTokenStopThresholdClient().setTokenStopThreshold(
            runId,
            new SetTokenStopThresholdRequest({ provider, thresholdTokens: thresholdTokens ?? undefined }),
          ),
        { toMessage: extractSafeErrorDetail },
      ),
    [run],
  )

  return { saving: busy, error, save }
}
