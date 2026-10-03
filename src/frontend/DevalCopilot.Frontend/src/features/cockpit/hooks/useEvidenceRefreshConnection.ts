import { useCallback } from 'react'
import { useOwnedLifetime, useOwnedState } from './useOwnedLifetime'

interface Connection {
  generation: number
}

function createConnection(): Connection {
  return { generation: 0 }
}

// A project and its run own one connection together: replacing either ends it and the replacement starts fresh.
const ownerOf = (projectId: string | null, runId: string | null) => (projectId === null ? null : JSON.stringify([projectId, runId]))

/**
 * The narrow link between the project-owned Refresh evidence action and the selected run's verification-dependent status reads. The
 * action calls `request`; the run passes `generation` on to the status hooks, which read again through their existing GETs when it
 * advances. It carries no data and starts no workflow request. The generation belongs to the committed project and run lifetime, so
 * another project or run, or a return to an earlier one, starts at zero, and a `request` retained from a replaced lifetime or
 * from an unmounted owner does nothing.
 */
export function useEvidenceRefreshConnection(projectId: string | null, runId: string | null) {
  const owner = useOwnedLifetime(ownerOf(projectId, runId))
  const [{ generation }, commit] = useOwnedState(owner, createConnection)
  const request = useCallback(() => {
    if (owner.key !== null) {
      commit((previous) => ({ generation: previous.generation + 1 }))
    }
  }, [owner, commit])

  return { generation, request }
}
