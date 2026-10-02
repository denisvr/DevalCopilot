import { useEffect } from 'react'
import { verificationExecutionOutputClient } from '../../../api/clients'
import { useOwnedLifetime, useOwnedState } from './useOwnedLifetime'

export type VerificationOutputStream = 'stdout' | 'stderr'

interface OutputFrame {
  text: string
  isFinal: boolean
  error: string | null
}

function createFrame(): OutputFrame {
  return { text: '', isFinal: false, error: null }
}

/** Output belongs to exactly one project, execution, stream and enabled state: a different one
 * starts from empty text in the very render that selects it, and a chunk or failure that was
 * requested for an earlier one can no longer be shown. */
export function useVerificationExecutionOutput(
  projectId: string,
  executionId: string | null,
  stream: VerificationOutputStream,
  enabled: boolean,
) {
  const owner = useOwnedLifetime(executionId && enabled ? JSON.stringify([projectId, executionId, stream]) : null)
  const [frame, commit] = useOwnedState(owner, createFrame)

  useEffect(() => {
    if (!executionId || !enabled) {
      return
    }

    let cancelled = false
    let timer: ReturnType<typeof setTimeout> | undefined
    let offset = 0

    async function readNext() {
      // A timer can fire after the owner ended but before this effect is cleaned up.
      if (cancelled || !owner.isActive()) {
        return
      }
      try {
        const result = await verificationExecutionOutputClient().getVerificationExecutionOutput(
          projectId,
          executionId!,
          stream,
          offset,
          16 * 1024,
        )
        if (cancelled || !owner.isActive()) {
          return
        }

        if (result.text) {
          commit((previous) => ({ ...previous, text: previous.text + result.text }))
        }
        offset = result.nextOffset ?? offset
        const caughtUp = (result.isFinal ?? false) && !result.text
        if (caughtUp) {
          commit((previous) => ({ ...previous, isFinal: true }))
          return
        }

        timer = setTimeout(() => void readNext(), 250)
      } catch {
        if (!cancelled) {
          commit((previous) => ({ ...previous, error: 'This verification output could not be loaded.' }))
        }
      }
    }

    void readNext()
    return () => {
      cancelled = true
      if (timer) {
        clearTimeout(timer)
      }
    }
  }, [enabled, executionId, projectId, stream, owner, commit])

  return frame
}
