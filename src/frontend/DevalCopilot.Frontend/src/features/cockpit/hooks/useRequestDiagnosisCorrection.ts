import { useCallback } from 'react'
import { RequestDiagnosisCorrectionRequest } from '../../../api/generated/api-client'
import { requestDiagnosisCorrectionClient } from '../../../api/clients'
import { describeVerificationDiagnosisFailure } from '../verificationDiagnosisFailure'
import { useRunScopedAction } from './useRunScopedAction'

interface UseRequestDiagnosisCorrectionResult {
  requesting: boolean
  error: string | null
  /** No guidance and no authorization exist for this source: only the exact diagnosis attempt is sent. */
  request: (runId: string, verificationDiagnosisAttemptId: string) => Promise<boolean>
}

const GENERIC_MESSAGE = 'A correction of the diagnosed findings could not be requested for this run.'

/** The run and the diagnosis attempt together own one request's lifetime; replacing either ends the old one. */
const ownerOf = (runId: string, verificationDiagnosisAttemptId: string | null) =>
  JSON.stringify([runId, verificationDiagnosisAttemptId])

/** Requests one durable Claude correction of a specific verification-diagnosis attempt's findings
 * (the host records the human escalation instead once the shared correction allowance is spent),
 * then triggers the caller's own status refresh.
 * The request is bound to the interaction lifetime of `currentRunId` AND
 * `currentVerificationDiagnosisAttemptId`: an obsolete completion, a foreign run or diagnosis, or
 * a duplicate of an in-flight submission never changes the current state. An accepted obsolete
 * request stays a real server operation. */
export function useRequestDiagnosisCorrection(
  currentRunId: string,
  currentVerificationDiagnosisAttemptId: string | null,
  onRequested: () => void,
): UseRequestDiagnosisCorrectionResult {
  const { busy, error, run } = useRunScopedAction(ownerOf(currentRunId, currentVerificationDiagnosisAttemptId))

  const request = useCallback(
    (runId: string, verificationDiagnosisAttemptId: string) =>
      run(
        ownerOf(runId, verificationDiagnosisAttemptId),
        () =>
          requestDiagnosisCorrectionClient().requestDiagnosisCorrection(
            runId,
            new RequestDiagnosisCorrectionRequest({ verificationDiagnosisAttemptId }),
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
