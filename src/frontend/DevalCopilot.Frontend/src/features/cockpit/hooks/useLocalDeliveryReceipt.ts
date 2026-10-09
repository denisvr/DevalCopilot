import { useCallback, useEffect } from 'react'
import { localDeliveryReceiptClient } from '../../../api/clients'
import { receiptSourceKey, toLocalDeliveryReading } from '../localDeliveryReceipt'
import type { LocalDeliveryReading, ReceiptSource } from '../localDeliveryReceipt'
import { useOwnedLifetime, useOwnedState } from './useOwnedLifetime'

export type LocalDeliveryReceiptFailure = 'read' | 'inconsistent'

export interface UseLocalDeliveryReceiptResult {
  /** The recorded reading; null while loading and after any failure. Never a reading of another source. */
  reading: LocalDeliveryReading | null
  loading: boolean
  /** `read`: the request failed. `inconsistent`: the host answered, but not for exactly this source. */
  failure: LocalDeliveryReceiptFailure | null
  /** Reads once more on explicit request; a reload retained from a replaced or unmounted lifetime starts nothing. */
  reload: () => void
}

interface Frame {
  reading: LocalDeliveryReading | null
  failure: LocalDeliveryReceiptFailure | null
  settledReload: number | null
  reloadCount: number
}

const createFrame = (): Frame => ({ reading: null, failure: null, settledReload: null, reloadCount: 0 })

/**
 * Reads the recorded receipt of one completed local-commit operation, once. The reading, failure and pending read belong to the
 * committed source's lifetime (run, operation, commit and checkpoint): another source, a return to an earlier one (A to B to A), a
 * null source or unmounting ends it, a replacement derives a fresh loading state in its first frame, and an older or replaced read
 * can neither overwrite nor settle a newer one. The immutable receipt is never polled, cached across lifetimes or retried
 * automatically; only `reload` reads again. A response that is not coherent for exactly this source is a failure, never a partial
 * reading.
 */
export function useLocalDeliveryReceipt(source: ReceiptSource | null): UseLocalDeliveryReceiptResult {
  const key = source === null ? null : receiptSourceKey(source)
  const owner = useOwnedLifetime(key)
  const [frame, commit] = useOwnedState(owner, createFrame)
  const reloadCount = frame.reloadCount
  const runId = source?.runId
  const operationId = source?.operationId
  const commitSha = source?.commitSha
  const checkpointId = source?.checkpointId
  const checkpointNumber = source?.checkpointNumber

  const reload = useCallback(() => {
    if (!owner.isActive() || owner.key === null) {
      return
    }
    commit((previous) => ({ ...previous, reloadCount: previous.reloadCount + 1 }))
  }, [owner, commit])

  useEffect(() => {
    if (
      runId === undefined
      || operationId === undefined
      || commitSha === undefined
      || checkpointId === undefined
      || checkpointNumber === undefined
    ) {
      return
    }

    const isCurrent = owner.begin('read')
    const expected: ReceiptSource = { runId, operationId, commitSha, checkpointId, checkpointNumber }
    const settle = (reading: LocalDeliveryReading | null, failure: LocalDeliveryReceiptFailure | null) => {
      if (isCurrent()) {
        commit((previous) => ({ ...previous, reading, failure, settledReload: reloadCount }))
      }
    }

    localDeliveryReceiptClient()
      .getLocalDeliveryReceipt(runId)
      .then(
        (response) => {
          const reading = toLocalDeliveryReading(response, expected)
          settle(reading, reading === null ? 'inconsistent' : null)
        },
        () => settle(null, 'read'),
      )
  }, [owner, commit, runId, operationId, commitSha, checkpointId, checkpointNumber, reloadCount])

  return {
    reading: frame.reading,
    loading: key !== null && frame.settledReload !== reloadCount,
    failure: frame.failure,
    reload,
  }
}
