import { useCallback } from 'react'
import { RequestLocalCommitRequest, requestLocalCommitClient } from '../../../api/clients'
import {
  LOCAL_COMMIT_UNKNOWN_OUTCOME_MESSAGE,
  classifyLocalCommitFailure,
  localCommitIdentityKey,
} from '../describeLocalCommit'
import type { LocalCommitIdentity } from '../describeLocalCommit'
import { useRunScopedAction } from './useRunScopedAction'

interface UseRequestLocalCommitResult {
  requesting: boolean
  error: string | null
  /** Resolves true only when the server admitted the request AND this owner is still current. */
  submit: (requestRunId: string, identity: LocalCommitIdentity, message: string, operationId: string) => Promise<boolean>
}

/**
 * Admits the explicit local commit of one committed approval identity. The run, `identityKey` and the draft version own the whole interaction
 * (request guard, pending state, error): replacing either ends the owner, A to B to A is a new owner, an obsolete completion
 * never touches a replacement, and a second submission in flight on one owner is ignored.
 *
 * The request is never resent automatically. A failure with no HTTP response, or a 5xx, has an unknown outcome: the host may have
 * admitted it, so the recorded status is read again (read-only reconciliation) and a fixed message says so. An accepted request
 * refreshes the status and then the cockpit; those follow-up reads are never awaited, so a failed read cannot turn an accepted
 * request into a reported failure. Fixed copy is chosen from the status and the stable problem code only.
 */
export function useRequestLocalCommit(
  runId: string,
  identityKey: string,
  draftVersion: number,
  refreshStatus: () => void,
  onSaved?: () => unknown,
): UseRequestLocalCommitResult {
  const ownerKey = JSON.stringify([runId, identityKey, draftVersion])
  const { busy, error, run } = useRunScopedAction(ownerKey)

  const submit = useCallback(
    async (requestRunId: string, identity: LocalCommitIdentity, message: string, operationId: string) => {
      if (requestRunId !== runId || localCommitIdentityKey(requestRunId, identity) !== identityKey) {
        return false
      }
      let unknownOutcome = false
      const accepted = await run(
        ownerKey,
        async () => {
          try {
            await requestLocalCommitClient().requestLocalCommit(
              requestRunId,
              new RequestLocalCommitRequest({
                operationId,
                checkpointId: identity.checkpointId,
                codeReviewAttemptId: identity.codeReviewAttemptId,
                humanCheckpointReviewId: identity.humanCheckpointReviewId,
                message,
              }),
            )
          } catch (caught: unknown) {
            unknownOutcome = classifyLocalCommitFailure(caught).kind === 'unknown'
            throw caught
          }
          // Read-only and owned by the status hook, so an obsolete owner cannot misuse it; the admission itself is real either way.
          refreshStatus()
        },
        {
          toMessage: (caught) => {
            const failure = classifyLocalCommitFailure(caught)
            return failure.kind === 'unknown' ? LOCAL_COMMIT_UNKNOWN_OUTCOME_MESSAGE : failure.message
          },
          onSuccess: () => {
            if (onSaved) {
              try {
                void Promise.resolve(onSaved()).catch(() => undefined)
              } catch {
                // The cockpit refresh reports its own failure; it never changes the admitted request's outcome.
              }
            }
          },
        },
      )
      if (!accepted && unknownOutcome) {
        refreshStatus()
      }
      return accepted
    },
    [run, runId, identityKey, ownerKey, refreshStatus, onSaved],
  )

  return { requesting: busy, error, submit }
}
