import { useCallback } from 'react'
import type { AgentAttemptArtifactMetadataResponse } from '../../../api/clients'
import { useAgentAttemptArtifactWindow } from '../hooks/useAgentAttemptArtifactWindow'
import type { AgentAttemptArtifactWindowTarget } from '../hooks/useAgentAttemptArtifactWindow'
import { useOwnedLifetime, useOwnedState } from '../hooks/useOwnedLifetime'
import { SealedArtifactWindowView } from './SealedArtifactWindowView'

interface AgentAttemptArtifactViewerProps {
  target: AgentAttemptArtifactWindowTarget
  /** The selected attempt's own validated artifact metadata; only a purpose present here is offered. */
  artifacts: readonly AgentAttemptArtifactMetadataResponse[]
}

const noPurpose = (): string | null => null

/**
 * On-demand viewer for the sealed artifacts of an Agent attempt selected from a run's attempt history, fetched through the attempt
 * route (independent of any collaboration message). Text is requested only after a purpose is selected and further windows only on
 * request. The chosen purpose belongs to the attempt's selection lifetime, so another attempt, a return to an earlier one, a close or
 * another run shows no purpose and no text in its first frame. Rendered as plain text only and never stored in the browser.
 */
export function AgentAttemptArtifactViewer({ target, artifacts }: AgentAttemptArtifactViewerProps) {
  const owner = useOwnedLifetime(target.selectionKey)
  const [selectedPurpose, commitPurpose] = useOwnedState(owner, noPurpose)
  const { state, loadNextWindow } = useAgentAttemptArtifactWindow(target, selectedPurpose)
  const selectPurpose = useCallback(
    (purpose: string | null) => {
      if (owner.isActive()) {
        commitPurpose(() => purpose)
      }
    },
    [owner, commitPurpose],
  )

  return (
    <SealedArtifactWindowView
      artifacts={artifacts}
      selectedPurpose={selectedPurpose}
      onSelectPurpose={selectPurpose}
      state={state}
      loadNextWindow={loadNextWindow}
    />
  )
}
