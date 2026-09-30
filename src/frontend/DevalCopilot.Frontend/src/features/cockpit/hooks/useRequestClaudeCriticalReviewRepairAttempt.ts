import { useCallback, useEffect, useRef, useState } from 'react'
import { requestClaudeCriticalReviewRepairAttemptClient } from '../../../api/clients'
import { extractSafeErrorDetail } from './useRequestCodexPlanningAttempt'

interface UseRequestClaudeCriticalReviewRepairAttemptResult {
  requesting: boolean
  error: string | null
  request: (sourceAttemptId: string) => Promise<boolean>
}

const GENERIC_MESSAGE = 'A critical review repair attempt could not be requested for this run.'

/** The request state, tagged with the run it belongs to so a request started for one run can
 * never show its in-flight state or its error on a different run. */
interface RepairRequestState {
  runId: string
  requesting: boolean
  error: string | null
}

/**
 * Requests the one manual format repair of the given critical review attempt for `runId`, then
 * triggers the caller's own status refresh. The server alone decides eligibility; this hook only
 * relays the request and surfaces the backend's fixed, safe error text.
 */
export function useRequestClaudeCriticalReviewRepairAttempt(
  runId: string,
  onRequested: () => void,
): UseRequestClaudeCriticalReviewRepairAttemptResult {
  const [state, setState] = useState<RepairRequestState | null>(null)
  // Bumped by every new request and by every run switch or unmount, so only the latest request of
  // the current run may write state or trigger the refresh. Ignoring a stale completion does not
  // cancel a request the server already accepted; the next status read shows its real outcome.
  const generation = useRef(0)

  useEffect(
    () => () => {
      generation.current += 1
      setState(null)
    },
    [runId],
  )

  const request = useCallback(
    async (sourceAttemptId: string) => {
      const mine = ++generation.current
      setState({ runId, requesting: true, error: null })
      try {
        await requestClaudeCriticalReviewRepairAttemptClient().requestClaudeCriticalReviewRepairAttempt(runId, sourceAttemptId)
        if (mine !== generation.current) return false
        setState({ runId, requesting: false, error: null })
        onRequested()
        return true
      } catch (caught: unknown) {
        if (mine !== generation.current) return false
        setState({ runId, requesting: false, error: extractSafeErrorDetail(caught, GENERIC_MESSAGE) })
        return false
      }
    },
    [runId, onRequested],
  )

  const belongsToCurrentRun = state?.runId === runId
  return {
    requesting: belongsToCurrentRun ? state.requesting : false,
    error: belongsToCurrentRun ? state.error : null,
    request,
  }
}
