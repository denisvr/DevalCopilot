import { useCallback, useEffect } from 'react'
import type { VerificationDiagnosisStatusResponse } from '../../../api/clients'
import { verificationDiagnosisStatusClient } from '../../../api/clients'
import { useOwnedLifetime, useOwnedState } from './useOwnedLifetime'

export interface UseVerificationDiagnosisStatusResult {
  status: VerificationDiagnosisStatusResponse | null
  loading: boolean
  error: string | null
  refresh: () => void
}

/** Everything the hook exposes belongs to one run's lifetime. `settledKey` names the read that last
 * settled (sequence and refresh count), so loading is derived: a read is loading until a result for
 * its own key is committed. `refreshCount` lives here too, so a new lifetime always starts at zero. */
interface Frame {
  status: VerificationDiagnosisStatusResponse | null
  error: string | null
  settledKey: string | null
  refreshCount: number
}

const createFrame = (): Frame => ({ status: null, error: null, settledKey: null, refreshCount: 0 })

const readKeyOf = (sequence: number | undefined, refreshCount: number) => JSON.stringify([sequence, refreshCount])

/**
 * Reads the verification-failure diagnosis state for a run. Unlike the attempt-only status hooks
 * the response is kept even when the run has no diagnosis yet, because it also carries the
 * display hints for what could be diagnosed now. `latestEventSequence` re-triggers a fetch
 * whenever any run event advances, and `refresh` lets a caller force one immediately after a
 * request, since claiming an attempt does not itself emit a run event.
 *
 * Status, error and the pending read belong to the run's lifetime: the committed render of a new
 * run (or a return to an earlier one) derives a fresh loading state, older overlapping reads are
 * ignored, and a `refresh` retained from a replaced lifetime issues no request.
 */
export function useVerificationDiagnosisStatus(
  runId: string | null,
  latestEventSequence: number | undefined,
): UseVerificationDiagnosisStatusResult {
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
    const readKey = readKeyOf(latestEventSequence, refreshCount)

    verificationDiagnosisStatusClient()
      .getVerificationDiagnosisStatus(runId)
      .then((response) => {
        if (isCurrent()) {
          commit((previous) => ({ ...previous, status: response, error: null, settledKey: readKey }))
        }
      })
      .catch(() => {
        if (isCurrent()) {
          commit((previous) => ({
            ...previous,
            status: null,
            error: 'Verification diagnosis status is unavailable.',
            settledKey: readKey,
          }))
        }
      })
  }, [owner, commit, runId, latestEventSequence, refreshCount])

  return {
    status: frame.status,
    error: frame.error,
    loading: runId !== null && frame.settledKey !== readKeyOf(latestEventSequence, refreshCount),
    refresh,
  }
}
