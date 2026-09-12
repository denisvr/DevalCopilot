import { useEffect, useRef, useState } from 'react'

/**
 * Interpolates the autonomous session timer between server refreshes. The
 * authoritative value always comes from the last fetched cockpit projection; this
 * only smooths the display for the seconds in between.
 */
export function useTickingDuration(baseSeconds: number, isRunning: boolean): number {
  const [displaySeconds, setDisplaySeconds] = useState(baseSeconds)
  const baseRef = useRef(baseSeconds)
  const baseSetAtRef = useRef(Date.now())

  useEffect(() => {
    baseRef.current = baseSeconds
    baseSetAtRef.current = Date.now()
    setDisplaySeconds(baseSeconds)
  }, [baseSeconds])

  useEffect(() => {
    if (!isRunning) {
      return
    }

    const interval = window.setInterval(() => {
      setDisplaySeconds(baseRef.current + (Date.now() - baseSetAtRef.current) / 1000)
    }, 1000)

    return () => window.clearInterval(interval)
  }, [isRunning])

  return displaySeconds
}
