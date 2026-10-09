import { useCallback } from 'react'
import { AbandonManualRunRequest, abandonManualRunClient } from '../../../api/clients'
import { ABANDON_UNKNOWN_OUTCOME_MESSAGE, classifyAbandonFailure } from '../describeRunAbandonment'
import { useRunScopedAction } from './useRunScopedAction'

interface UseAbandonManualRunResult {
  requesting: boolean
  error: string | null
  /** Resolves true only when the server recorded the abandonment AND this owner is still current. */
  submit: (requestRunId: string, reason: string) => Promise<boolean>
}

/**
 * Records the explicit abandonment of one run. The run, the eligible-form identity key and the draft version own the whole
 * interaction (request guard, pending state, error): replacing any of them ends the owner, A to B to A is a new owner, an obsolete
 * completion never touches a replacement, and a second submission in flight on one owner is ignored.
 *
 * The request is never resent automatically and nothing is cancelled by it. A failure with no HTTP response, or a 5xx, has an
 * unknown outcome: the host may have recorded it, so the read-only status is read again (read-only reconciliation) and a fixed
 * message says so. A recorded abandonment refreshes the status and the project list before the owner check, so an accepted request
 * stays real even when its owner was replaced; the cockpit refresh runs only for a current owner. Those follow-up reads are never
 * awaited, so a failed read cannot turn an accepted request into a reported failure. Fixed copy is chosen from the status and the
 * stable problem code only.
 */
export function useAbandonManualRun(
  runId: string,
  identityKey: string,
  draftVersion: number,
  refreshStatus: () => void,
  refreshProject?: () => void,
  onSaved?: () => unknown,
): UseAbandonManualRunResult {
  const ownerKey = JSON.stringify([runId, identityKey, draftVersion])
  const { busy, error, run } = useRunScopedAction(ownerKey)

  const submit = useCallback(
    async (requestRunId: string, reason: string) => {
      if (requestRunId !== runId || identityKey === '') {
        return false
      }
      let unknownOutcome = false
      const accepted = await run(
        ownerKey,
        async () => {
          try {
            await abandonManualRunClient().abandonManualRun(requestRunId, new AbandonManualRunRequest({ reason }))
          } catch (caught: unknown) {
            unknownOutcome = classifyAbandonFailure(caught).kind === 'unknown'
            throw caught
          }
          // Read-only and owned by their own lifetimes, so an obsolete owner cannot misuse them; the abandonment is real either way.
          refreshStatus()
          try {
            refreshProject?.()
          } catch {
            // The project list reports its own failure; it never changes the recorded abandonment's outcome.
          }
        },
        {
          toMessage: (caught) => {
            const failure = classifyAbandonFailure(caught)
            return failure.kind === 'unknown' ? ABANDON_UNKNOWN_OUTCOME_MESSAGE : failure.message
          },
          onSuccess: () => {
            if (onSaved) {
              try {
                void Promise.resolve(onSaved()).catch(() => undefined)
              } catch {
                // The cockpit refresh reports its own failure; it never changes the recorded abandonment's outcome.
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
    [run, runId, identityKey, ownerKey, refreshStatus, refreshProject, onSaved],
  )

  return { requesting: busy, error, submit }
}
