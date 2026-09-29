import { useCallback, useState } from 'react'
import { requestCodexPlanningRepairAttemptClient } from '../../../api/clients'
import { extractSafeErrorDetail } from './useRequestCodexPlanningAttempt'

interface UseRequestCodexPlanningRepairAttemptResult {
  requesting: boolean
  error: string | null
  request: (sourceAttemptId: string) => Promise<boolean>
}

const GENERIC_MESSAGE = 'A repair attempt could not be requested for this run.'

/** The request state, tagged with the run it belongs to so a request started for one run can
 * never show its in-flight state or its error on a different run. */
interface RepairRequestState {
  runId: string
  requesting: boolean
  error: string | null
}

/**
 * Requests the one manual format repair of the given Codex planning attempt for `runId`, then
 * triggers the caller's own status refresh. The server alone decides eligibility; this hook only
 * relays the request and surfaces the backend's fixed, safe error text.
 */
export function useRequestCodexPlanningRepairAttempt(
  runId: string,
  onRequested: () => void,
): UseRequestCodexPlanningRepairAttemptResult {
  const [state, setState] = useState<RepairRequestState | null>(null)

  const request = useCallback(
    async (sourceAttemptId: string) => {
      setState({ runId, requesting: true, error: null })
      try {
        await requestCodexPlanningRepairAttemptClient().requestCodexPlanningRepairAttempt(runId, sourceAttemptId)
        setState({ runId, requesting: false, error: null })
        onRequested()
        return true
      } catch (caught: unknown) {
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
