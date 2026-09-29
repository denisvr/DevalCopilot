import { useState } from 'react'
import type { AgentAttemptArtifactMetadataResponse } from '../../../api/clients'
import { useAgentAttemptArtifactWindow } from '../hooks/useAgentAttemptArtifactWindow'
import { SealedArtifactWindowView } from './SealedArtifactWindowView'

interface AgentAttemptArtifactViewerProps {
  runId: string
  attemptId: string
  /** The selected attempt's own bounded artifact metadata; only a purpose present here is offered. */
  artifacts: readonly AgentAttemptArtifactMetadataResponse[]
}

/**
 * On-demand viewer for the sealed artifacts of an Agent attempt selected from the run history,
 * fetched through the attempt route (independent of any collaboration message). Text is requested
 * only after a purpose is selected and further windows only on request; the parent keys this by run
 * and attempt, so accumulated text never survives a run, attempt, or close change. Rendered as plain
 * text only and never stored in the browser.
 */
export function AgentAttemptArtifactViewer({ runId, attemptId, artifacts }: AgentAttemptArtifactViewerProps) {
  const [selectedPurpose, setSelectedPurpose] = useState<string | null>(null)
  const { state, loadNextWindow } = useAgentAttemptArtifactWindow(runId, attemptId, selectedPurpose)

  return (
    <SealedArtifactWindowView
      artifacts={artifacts}
      selectedPurpose={selectedPurpose}
      onSelectPurpose={setSelectedPurpose}
      state={state}
      loadNextWindow={loadNextWindow}
    />
  )
}
