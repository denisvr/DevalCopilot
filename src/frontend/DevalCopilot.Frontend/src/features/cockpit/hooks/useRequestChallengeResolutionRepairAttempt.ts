import { useCallback } from 'react'
import { requestChallengeResolutionRepairAttemptClient } from '../../../api/clients'
import { extractSafeErrorDetail } from './useRequestCodexPlanningAttempt'
import { useRunScopedAction } from './useRunScopedAction'

interface UseRequestChallengeResolutionRepairAttemptResult {
  requesting: boolean
  error: string | null
  request: (sourceAttemptId: string) => Promise<boolean>
}

const GENERIC_MESSAGE = 'A challenge resolution repair attempt could not be requested for this run.'

/**
 * Requests the one manual format repair of the given challenge resolution attempt for `runId`, then
 * triggers the caller's own status refresh. The server alone decides eligibility; this hook only
 * relays the request and surfaces the backend's fixed, safe error text. The request is bound to
 * the run's interaction lifetime: a run switch or unmount drops the state, an obsolete completion
 * never writes state or refreshes, and a second submission of the same attempt while one is in
 * flight is ignored. An ignored completion does not cancel a request the server already accepted.
 */
export function useRequestChallengeResolutionRepairAttempt(
  runId: string,
  onRequested: () => void,
): UseRequestChallengeResolutionRepairAttemptResult {
  const { busy, error, run } = useRunScopedAction(runId)

  const request = useCallback(
    (sourceAttemptId: string) =>
      run(runId, () => requestChallengeResolutionRepairAttemptClient().requestChallengeResolutionRepairAttempt(runId, sourceAttemptId), {
        toMessage: (caught) => extractSafeErrorDetail(caught, GENERIC_MESSAGE),
        onSuccess: onRequested,
      }),
    [run, runId, onRequested],
  )

  return { requesting: busy, error, request }
}
