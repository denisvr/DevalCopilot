import { useCallback, useEffect } from 'react'
import type { CodeReviewAttemptStatusResponse } from '../../../api/clients'
import { codeReviewAttemptStatusClient } from '../../../api/clients'
import { useOwnedLifetime, useOwnedState } from './useOwnedLifetime'

export interface UseCodeReviewAttemptStatusResult {
  status: CodeReviewAttemptStatusResponse | null
  loading: boolean
  error: string | null
  refresh: () => void
}

/** Everything the hook exposes belongs to one run's lifetime. `settledKey` names the read that last settled (event sequence,
 * refresh count and evidence refresh generation), so loading is derived: a read is loading, in the very render that carries a
 * new key, until a result for that key is committed. `refreshCount` lives here too, so a new lifetime always starts at zero. */
interface Frame {
  status: CodeReviewAttemptStatusResponse | null
  error: string | null
  settledKey: string | null
  refreshCount: number
}

const createFrame = (): Frame => ({ status: null, error: null, settledKey: null, refreshCount: 0 })

const readKeyOf = (sequence: number | undefined, refreshCount: number, evidenceRefreshGeneration: number) =>
  JSON.stringify([sequence, refreshCount, evidenceRefreshGeneration])

/**
 * Reads the most recent Codex code-review attempt for a run. `latestEventSequence` re-triggers a fetch whenever any run event
 * advances, and `refresh` lets a caller force one immediately after successfully requesting a new attempt, since claiming an
 * attempt does not itself emit a run event. `evidenceRefreshGeneration` is advanced by the project's explicit Refresh evidence
 * action and reads the status again the same way.
 *
 * Status, error and the pending read belong to the run's committed lifetime (nothing is owned while the run is null): the
 * committed render of a new run, or a return to an earlier one, derives a fresh loading state and never exposes the previous run's
 * status, older overlapping reads are ignored, and a `refresh` retained from a replaced or unmounted lifetime starts no request. A
 * failed read masks the status and reports a safe error: it is never treated as "no previous review".
 */
export function useCodeReviewAttemptStatus(
  runId: string | null,
  latestEventSequence: number | undefined,
  evidenceRefreshGeneration = 0,
): UseCodeReviewAttemptStatusResult {
  const owner = useOwnedLifetime(runId)
  const [frame, commit] = useOwnedState(owner, createFrame)
  const refreshCount = frame.refreshCount

  const refresh = useCallback(() => {
    if (!owner.isActive() || owner.key === null) {
      return
    }
    commit((previous) => ({ ...previous, refreshCount: previous.refreshCount + 1 }))
  }, [owner, commit])

  useEffect(() => {
    if (runId === null) {
      return
    }

    const isCurrent = owner.begin('read')
    const readKey = readKeyOf(latestEventSequence, refreshCount, evidenceRefreshGeneration)

    codeReviewAttemptStatusClient()
      .getCodeReviewAttemptStatus(runId)
      .then((response) => {
        if (isCurrent()) {
          commit((previous) => ({ ...previous, status: response.hasAttempt ? response : null, error: null, settledKey: readKey }))
        }
      })
      .catch(() => {
        if (isCurrent()) {
          commit((previous) => ({
            ...previous,
            status: null,
            error: 'Code review attempt status is unavailable.',
            settledKey: readKey,
          }))
        }
      })
  }, [owner, commit, runId, latestEventSequence, refreshCount, evidenceRefreshGeneration])

  return {
    status: frame.status,
    error: frame.error,
    loading: runId !== null && frame.settledKey !== readKeyOf(latestEventSequence, refreshCount, evidenceRefreshGeneration),
    refresh,
  }
}
