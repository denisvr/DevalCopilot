import { useCallback } from 'react'
import { AuthorizePlanningImplementationRequest } from '../../../api/generated/api-client'
import { authorizePlanningImplementationClient } from '../../../api/clients'
import { describePlanningAuthorizationFailure } from '../planningAuthorizationFailure'
import { useRunScopedAction } from './useRunScopedAction'

interface UseAuthorizePlanningImplementationResult {
  authorizing: boolean
  error: string | null
  /** Sends the raw rationale; the server normalizes, bounds, and decides. Resolves true only when the server accepted
   * it AND the submission still belongs to the current interaction lifetime. */
  authorize: (runId: string, escalationMessageId: string, rationale: string) => Promise<boolean>
}

/** The run and the escalation together own one authorization's lifetime; replacing either ends the old one. */
const ownerOf = (runId: string, escalationMessageId: string) => JSON.stringify([runId, escalationMessageId])

/**
 * Records the one explicit human authorization for an escalated final plan. It starts no implementation and spends
 * nothing. Bound to the interaction lifetime of `currentRunId` and `currentEscalationMessageId`: an obsolete completion,
 * a foreign run or escalation, or a duplicate of an in-flight submission changes nothing here and resolves false. An
 * accepted obsolete request stays a real server operation; the next authoritative read shows its real outcome.
 */
export function useAuthorizePlanningImplementation(
  currentRunId: string,
  currentEscalationMessageId: string,
  onAuthorized: () => void,
): UseAuthorizePlanningImplementationResult {
  const { busy, error, run } = useRunScopedAction(ownerOf(currentRunId, currentEscalationMessageId))

  const authorize = useCallback(
    (runId: string, escalationMessageId: string, rationale: string) =>
      run(
        ownerOf(runId, escalationMessageId),
        () =>
          authorizePlanningImplementationClient().authorizePlanningImplementation(
            runId,
            escalationMessageId,
            new AuthorizePlanningImplementationRequest({ rationale }),
          ),
        { toMessage: describePlanningAuthorizationFailure, onSuccess: onAuthorized },
      ),
    [run, onAuthorized],
  )

  return { authorizing: busy, error, authorize }
}
