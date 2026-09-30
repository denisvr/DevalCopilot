import { useCallback } from 'react'
import { ApiException, SetClaudeMutationTurnLimitRequest } from '../../../api/generated/api-client'
import { setClaudeMutationTurnLimitClient } from '../../../api/clients'
import { useRunScopedAction } from './useRunScopedAction'
import { parseClaudeTurnLimitDraft } from '../describeClaudeTurnLimit'

interface UseSetClaudeMutationTurnLimitResult {
  saving: boolean
  error: string | null
  /** Saves a draft (empty string clears). Resolves true only when the server accepted it. */
  save: (draft: string) => Promise<boolean>
  /** Clears the run's request by sending an explicit null. */
  clear: () => Promise<boolean>
}

export const TURN_LIMIT_VALIDATION_MESSAGE = 'Enter a whole number from 1 to 100.'
const GENERIC_MESSAGE = 'The Claude turn limit request could not be saved for this run.'

/** Fixed, safe messages chosen by HTTP status only; server text is never displayed. */
function safeMessage(caught: unknown): string {
  if (!ApiException.isApiException(caught)) {
    return GENERIC_MESSAGE
  }
  switch ((caught as ApiException).status) {
    case 400:
      return 'The Claude turn limit must be a whole number from 1 to 100.'
    case 404:
      return 'This run could not be found.'
    case 409:
      return 'The Claude turn limit request changed concurrently. Reload the run and retry.'
    case 422:
      return "This run's Claude turn limit request can no longer be changed."
    default:
      return GENERIC_MESSAGE
  }
}

/**
 * Sets or clears the run-scoped Claude agentic-turn request for future Claude implementation and
 * review-correction attempts. It is a request for a provider-loop guardrail, not an account,
 * token, or cost ceiling, and never affects an already-claimed attempt. Clearing sends an explicit
 * JSON null because the server rejects a missing member. The pending flag and error belong to the
 * run's mounted interaction lifetime: a switch or unmount drops them (returning to an earlier run
 * never restores an old error), an obsolete completion changes nothing, and a second submission
 * while one is in flight is ignored. A resolved false is never an authorization for a caller to
 * refresh, clear a draft, or show success.
 */
export function useSetClaudeMutationTurnLimit(runId: string): UseSetClaudeMutationTurnLimitResult {
  const { busy, error, run, reportError } = useRunScopedAction(runId)

  const send = useCallback(
    (maxTurns: number | null) =>
      run(
        runId,
        // fromJS keeps a null member, so the serialized body is {'maxTurns':null} for a clear.
        () => setClaudeMutationTurnLimitClient().setClaudeMutationTurnLimit(runId, SetClaudeMutationTurnLimitRequest.fromJS({ maxTurns })),
        { toMessage: safeMessage },
      ),
    [run, runId],
  )

  const save = useCallback(
    async (draft: string) => {
      const parsed = parseClaudeTurnLimitDraft(draft)
      if (parsed.kind === 'invalid') {
        reportError(TURN_LIMIT_VALIDATION_MESSAGE)
        return false
      }
      return send(parsed.kind === 'set' ? parsed.maxTurns : null)
    },
    [reportError, send],
  )

  const clear = useCallback(() => send(null), [send])

  return { saving: busy, error, save, clear }
}
