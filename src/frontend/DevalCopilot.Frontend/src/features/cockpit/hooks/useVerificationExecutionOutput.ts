import { useEffect, useState } from 'react'
import { verificationExecutionOutputClient } from '../../../api/clients'

export type VerificationOutputStream = 'stdout' | 'stderr'

export function useVerificationExecutionOutput(
  projectId: string,
  executionId: string | null,
  stream: VerificationOutputStream,
  enabled: boolean,
) {
  const [text, setText] = useState('')
  const [isFinal, setIsFinal] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    let cancelled = false
    let timer: ReturnType<typeof setTimeout> | undefined
    queueMicrotask(() => {
      if (!cancelled) {
        setText('')
        setIsFinal(false)
        setError(null)
      }
    })

    if (!executionId || !enabled) {
      return () => {
        cancelled = true
      }
    }

    let offset = 0

    async function readNext() {
      try {
        const result = await verificationExecutionOutputClient().getVerificationExecutionOutput(
          projectId,
          executionId!,
          stream,
          offset,
          16 * 1024,
        )
        if (cancelled) {
          return
        }

        if (result.text) {
          setText(previous => previous + result.text)
        }
        offset = result.nextOffset ?? offset
        const caughtUp = (result.isFinal ?? false) && !result.text
        if (caughtUp) {
          setIsFinal(true)
          return
        }

        timer = setTimeout(() => void readNext(), 250)
      } catch {
        if (!cancelled) {
          setError('This verification output could not be loaded.')
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
  }, [enabled, executionId, projectId, stream])

  return { text, isFinal, error }
}
