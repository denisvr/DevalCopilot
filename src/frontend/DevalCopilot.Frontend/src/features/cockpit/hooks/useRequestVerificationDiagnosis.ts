import { useCallback } from 'react'
import { RequestVerificationDiagnosisRequest } from '../../../api/generated/api-client'
import { requestVerificationDiagnosisClient } from '../../../api/clients'
import { describeVerificationDiagnosisFailure } from '../verificationDiagnosisFailure'
import { useRunScopedAction } from './useRunScopedAction'

interface UseRequestVerificationDiagnosisResult {
  requesting: boolean
  error: string | null
  request: (runId: string, executionReportMessageId: string) => Promise<boolean>
}

const GENERIC_MESSAGE = 'A diagnosis of the failed verification could not be requested for this run.'

/** The run and the report together own one request's lifetime; replacing either ends the old one. */
const ownerOf = (runId: string, executionReportMessageId: string | null) => JSON.stringify([runId, executionReportMessageId])

/** Requests one durable, read-only Codex diagnosis of the failed local verification of a specific
 * implementation ExecutionReport message, then triggers the caller's own status refresh.
 * The request is bound to the interaction lifetime of `currentRunId` AND
 * `currentExecutionReportMessageId`: an obsolete completion, a foreign run or report, or a
 * duplicate of an in-flight submission never changes the current state. An accepted obsolete
 * request stays a real server operation. A refusal reported before the latest evidence refresh
 * (`evidenceRefreshGeneration`) is no longer shown. */
export function useRequestVerificationDiagnosis(
  currentRunId: string,
  currentExecutionReportMessageId: string | null,
  onRequested: () => void,
  evidenceRefreshGeneration = 0,
): UseRequestVerificationDiagnosisResult {
  const { busy, error, run } = useRunScopedAction(ownerOf(currentRunId, currentExecutionReportMessageId), evidenceRefreshGeneration)

  const request = useCallback(
    (runId: string, executionReportMessageId: string) =>
      run(
        ownerOf(runId, executionReportMessageId),
        () =>
          requestVerificationDiagnosisClient().requestVerificationDiagnosis(
            runId,
            new RequestVerificationDiagnosisRequest({ executionReportMessageId }),
          ),
        {
          toMessage: (caught) => describeVerificationDiagnosisFailure(caught, GENERIC_MESSAGE),
          onSuccess: onRequested,
        },
      ),
    [run, onRequested],
  )

  return { requesting: busy, error, request }
}
