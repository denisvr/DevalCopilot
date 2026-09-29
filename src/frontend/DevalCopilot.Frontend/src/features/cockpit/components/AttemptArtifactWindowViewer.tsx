import { useState } from 'react'
import type { AgentAttemptArtifactMetadataResponse } from '../../../api/clients'
import { useSealedAgentArtifactWindow } from '../hooks/useSealedAgentArtifactWindow'
import { SealedArtifactWindowView } from './SealedArtifactWindowView'

interface AttemptArtifactWindowViewerProps {
  runId: string
  messageId: string
  /** This attempt's own bounded artifact metadata, exactly as the evidence drill-down already
   * loaded it — purpose selection is offered only for a purpose actually present here, never for
   * every allowlisted purpose unconditionally. */
  artifacts: readonly AgentAttemptArtifactMetadataResponse[]
}

/**
 * On-demand viewer for one bounded, integrity-verified text window of a sealed Agent-attempt
 * artifact reached through a collaboration message — an extension of the existing collaboration
 * evidence drill-down. Offers purpose selection and manual next-window loading only for artifact
 * metadata actually linked to this attempt (see `artifacts`). Fetched text is always rendered as
 * plain text (never HTML), and this component never writes it to `localStorage`/`sessionStorage`/
 * the URL.
 */
export function AttemptArtifactWindowViewer({ runId, messageId, artifacts }: AttemptArtifactWindowViewerProps) {
  const [selectedPurpose, setSelectedPurpose] = useState<string | null>(null)
  const { state, loadNextWindow } = useSealedAgentArtifactWindow(runId, messageId, selectedPurpose)

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
