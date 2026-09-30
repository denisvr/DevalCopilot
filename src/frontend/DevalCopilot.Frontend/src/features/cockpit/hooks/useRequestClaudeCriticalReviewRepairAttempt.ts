import { useCallback } from 'react'
import { requestClaudeCriticalReviewRepairAttemptClient } from '../../../api/clients'
import { extractSafeErrorDetail } from './useRequestCodexPlanningAttempt'
import { useRunScopedAction } from './useRunScopedAction'

interface UseRequestClaudeCriticalReviewRepairAttemptResult {
  requesting: boolean
  error: string | null
  request: (sourceAttemptId: string) => Promise<boolean>
}

const GENERIC_MESSAGE = 'A critical review repair attempt could not be requested for this run.'

/**
 * Requests the one manual format repair of the given critical review attempt for `runId`, then
 * triggers the caller's own status refresh. The server alone decides eligibility; this hook only
 * relays the request and surfaces the backend's fixed, safe error text. The request is bound to
 * the run's interaction lifetime: a run switch or unmount drops the state, an obsolete completion
 * never writes state or refreshes, and a second submission of the same attempt while one is in
 * flight is ignored. An ignored completion does not cancel a request the server already accepted.
 */
export function useRequestClaudeCriticalReviewRepairAttempt(
  runId: string,
  onRequested: () => void,
): UseRequestClaudeCriticalReviewRepairAttemptResult {
  const { busy, error, run } = useRunScopedAction(runId)

  const request = useCallback(
    (sourceAttemptId: string) =>
      run(runId, () => requestClaudeCriticalReviewRepairAttemptClient().requestClaudeCriticalReviewRepairAttempt(runId, sourceAttemptId), {
        toMessage: (caught) => extractSafeErrorDetail(caught, GENERIC_MESSAGE),
        onSuccess: onRequested,
      }),
    [run, runId, onRequested],
  )

  return { requesting: busy, error, request }
}
