import { useEffect, useRef, useState } from 'react'
import { processAttemptOutputClient } from '../../../api/clients'

export type ProcessOutputStream = 'stdout' | 'stderr'

interface UseProcessAttemptOutputResult {
  text: string
  status: string
  isFinal: boolean
  truncated: boolean
  error: string | null
}

/**
 * Polls the process-attempt output endpoint with an advancing byte-offset cursor — the same
 * shape as the run-event sequence catch-up elsewhere in this app — never a live push channel.
 * Stops polling once the underlying artifact reports itself sealed (`isFinal`) and this hook has
 * caught up to it (a poll returned no further text), matching the endpoint's own contract: a
 * sealed artifact can still take more than one bounded read to fully drain.
 */
export function useProcessAttemptOutput(
  runId: string | null,
  attemptId: string | null,
  stream: ProcessOutputStream,
  pollIntervalMs: number = 1000,
): UseProcessAttemptOutputResult {
  const [text, setText] = useState('')
  const [status, setStatus] = useState('Ok')
  const [isFinal, setIsFinal] = useState(false)
  const [truncated, setTruncated] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const offsetRef = useRef(0)

  useEffect(() => {
    offsetRef.current = 0
    setText('')
    setStatus('Ok')
    setIsFinal(false)
    setTruncated(false)
    setError(null)

    if (!runId || !attemptId) {
      return
    }

    let cancelled = false
    let timeoutHandle: ReturnType<typeof setTimeout> | undefined

    async function pollOnce() {
      try {
        const result = await processAttemptOutputClient().getProcessAttemptOutput(
          runId!,
          attemptId!,
          stream,
          offsetRef.current,
          undefined,
        )
        if (cancelled) return

        setStatus(result.status ?? 'Ok')
        setTruncated(result.truncated ?? false)
        if (result.text) {
          setText((previous) => previous + result.text)
        }

        const nextOffset = result.nextOffset ?? offsetRef.current
        const caughtUp = (result.isFinal ?? false) && (!result.text || result.text.length === 0)
        offsetRef.current = nextOffset

        if (caughtUp) {
          setIsFinal(true)
          return
        }

        timeoutHandle = setTimeout(pollOnce, pollIntervalMs)
      } catch (caught: unknown) {
        if (!cancelled) {
          setError(caught instanceof Error ? caught.message : 'Failed to load process output.')
        }
      }
    }

    void pollOnce()

    return () => {
      cancelled = true
      if (timeoutHandle) {
        clearTimeout(timeoutHandle)
      }
    }
  }, [runId, attemptId, stream, pollIntervalMs])

  return { text, status, isFinal, truncated, error }
}
