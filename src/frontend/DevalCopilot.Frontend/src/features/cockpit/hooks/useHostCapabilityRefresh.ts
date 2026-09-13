import { useCallback, useState } from 'react'
import { environmentClient } from '../../../api/clients'

interface UseHostCapabilityRefreshResult {
  refreshingCapability: string | null
  requestRefresh: (capability: string) => Promise<void>
}

/**
 * Requests a refresh of one shared host capability observation. Not project-scoped: refreshing
 * from any one project's view updates the one underlying snapshot every project projects from.
 */
export function useHostCapabilityRefresh(onRefreshed: () => void): UseHostCapabilityRefreshResult {
  const [refreshingCapability, setRefreshingCapability] = useState<string | null>(null)

  const requestRefresh = useCallback(
    async (capability: string) => {
      setRefreshingCapability(capability)
      try {
        await environmentClient().requestHostCapabilityRefresh(capability)
        onRefreshed()
      } finally {
        setRefreshingCapability(null)
      }
    },
    [onRefreshed],
  )

  return { refreshingCapability, requestRefresh }
}
