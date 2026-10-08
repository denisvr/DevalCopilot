import { useCallback, useEffect } from 'react'
import type { GetLocalCommitStatusResponse } from '../../../api/clients'
import { localCommitStatusClient } from '../../../api/clients'
import { useOwnedLifetime, useOwnedState } from './useOwnedLifetime'

export interface UseLocalCommitStatusResult {
  /** The recorded status; null while no read succeeded for this lifetime, and masked after a failed read. */
  status: GetLocalCommitStatusResponse | null
  loading: boolean
  error: string | null
  /** True only for a successful read of the CURRENT run, event sequence and evidence generation: a pending or failed refresh is not current. */
  current: boolean
  refresh: () => void
}

interface Frame {
  status: GetLocalCommitStatusResponse | null
  error: string | null
  settledKey: string | null
  refreshCount: number
}

const createFrame = (): Frame => ({ status: null, error: null, settledKey: null, refreshCount: 0 })

const readKeyOf = (sequence: number | undefined, refreshCount: number, evidenceRefreshGeneration: number) =>
  JSON.stringify([sequence, refreshCount, evidenceRefreshGeneration])

/**
 * Reads the advisory local-commit eligibility and the recorded operation of a run. The read repeats when the cockpit's latest event
 * sequence or the evidence refresh generation changes, and on `refresh` (used after a request, whose admission emits no event the
 * view is guaranteed to have seen). Status, error and the pending read belong to the run's committed lifetime: another run, or a
 * return to an earlier one, derives a fresh loading state and never exposes the previous status, older overlapping reads are
 * ignored, and a `refresh` retained from a replaced or unmounted lifetime starts no request. A failed read masks the status.
 */
export function useLocalCommitStatus(
  runId: string | null,
  latestEventSequence: number | undefined,
  evidenceRefreshGeneration = 0,
): UseLocalCommitStatusResult {
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

    localCommitStatusClient()
      .getLocalCommitStatus(runId)
      .then((response) => {
        if (isCurrent()) {
          commit((previous) => ({ ...previous, status: response, error: null, settledKey: readKey }))
        }
      })
      .catch(() => {
        if (isCurrent()) {
          commit((previous) => ({ ...previous, status: null, error: 'The local-commit status could not be read.', settledKey: readKey }))
        }
      })
  }, [owner, commit, runId, latestEventSequence, refreshCount, evidenceRefreshGeneration])

  const loading = runId !== null && frame.settledKey !== readKeyOf(latestEventSequence, refreshCount, evidenceRefreshGeneration)
  return {
    status: frame.status,
    error: frame.error,
    loading,
    current: runId !== null && !loading && frame.error === null && frame.status !== null,
    refresh,
  }
}
