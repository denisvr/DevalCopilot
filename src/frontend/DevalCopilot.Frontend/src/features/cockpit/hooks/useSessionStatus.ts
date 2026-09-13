import { useEffect, useSyncExternalStore } from 'react'
import { getSessionStatus, initializeLaunchSession, subscribeToSessionStatus, type SessionStatus } from '../../../api/session'

/** Starts session bootstrap once and re-renders as its status changes. */
export function useSessionStatus(): SessionStatus {
  useEffect(() => {
    void initializeLaunchSession()
  }, [])

  return useSyncExternalStore(subscribeToSessionStatus, getSessionStatus)
}
