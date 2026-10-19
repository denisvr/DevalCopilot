import { useEffect, useState } from 'react'

/**
 * How old, in whole seconds of the browser's monotonic clock, the validated reading is, re-evaluated every second while it is watched.
 * It measures only how long ago this window received the answer: it is never a heartbeat, never renews anything, and is null while
 * there is no reading. It is what turns a stalled transport into a stale display instead of an earlier success that looks current.
 */
export function useReadingAgeSeconds(receivedAtMilliseconds: number | null, watching: boolean): number | null {
  const [now, setNow] = useState(() => performance.now())

  useEffect(() => {
    if (receivedAtMilliseconds === null || !watching) {
      return
    }

    const interval = window.setInterval(() => setNow(performance.now()), 1000)
    return () => window.clearInterval(interval)
  }, [receivedAtMilliseconds, watching])

  return receivedAtMilliseconds === null ? null : Math.max(0, Math.floor((now - receivedAtMilliseconds) / 1000))
}
