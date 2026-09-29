import type { AgentAttemptArtifactMetadataResponse } from '../../../api/clients'
import type { SealedAgentArtifactWindowState } from '../hooks/useSealedArtifactWindow'
import { ARTIFACT_PURPOSE_LABELS, availableArtifactPurposes, sensitivityCaveatFor } from '../artifactPurposes'

interface SealedArtifactWindowViewProps {
  artifacts: readonly AgentAttemptArtifactMetadataResponse[]
  selectedPurpose: string | null
  onSelectPurpose: (purpose: string | null) => void
  state: SealedAgentArtifactWindowState
  loadNextWindow: () => void
}

/**
 * Presentation of one on-demand, bounded, integrity-verified sealed text window. Purpose selection
 * is offered only for a purpose actually present in `artifacts`, and text is always rendered as a
 * plain React text node inside `<pre>` — never HTML or Markdown. The owning container holds the
 * fetch state; this component never touches browser storage, the URL, or logs.
 */
export function SealedArtifactWindowView({
  artifacts,
  selectedPurpose,
  onSelectPurpose,
  state,
  loadNextWindow,
}: SealedArtifactWindowViewProps) {
  const availablePurposes = availableArtifactPurposes(artifacts)

  if (availablePurposes.length === 0) {
    return null
  }

  return (
    <div className="dc-artifact-window-viewer">
      <label>
        Artifact:{' '}
        <select
          value={selectedPurpose ?? ''}
          onChange={(event) => onSelectPurpose(event.target.value === '' ? null : event.target.value)}
        >
          <option value="">Select an artifact…</option>
          {availablePurposes.map((purpose) => (
            <option key={purpose} value={purpose}>
              {ARTIFACT_PURPOSE_LABELS[purpose]}
            </option>
          ))}
        </select>
      </label>
      {selectedPurpose && (
        <>
          <p className="dc-card-evidence-caveat">
            {sensitivityCaveatFor(selectedPurpose)} Shown here as plain text only, never HTML.
          </p>
          {state.status === 'idle' && (
            <p>
              <button type="button" onClick={() => loadNextWindow()}>
                Load
              </button>
            </p>
          )}
          {state.status === 'loading' && <p className="dc-empty-state">Loading artifact text…</p>}
          {state.status === 'unavailable' && (
            <p className="dc-empty-state">No sealed artifact is available for this purpose.</p>
          )}
          {state.status === 'missing' && (
            <p className="dc-empty-state" role="status">
              This artifact's sealed file could not be verified and is not shown.
            </p>
          )}
          {state.status === 'integrity-mismatch' && (
            <p className="dc-empty-state" role="status">
              This artifact failed integrity verification and is not shown.
            </p>
          )}
          {state.status === 'identity-invalid' && (
            <p className="dc-empty-state" role="status">
              This attempt's recorded identity could not be verified, so its artifacts are not shown.
            </p>
          )}
          {state.status === 'error' && (
            <p className="dc-card-evidence-error" role="status">
              {state.message}{' '}
              <button type="button" onClick={() => loadNextWindow()}>
                Retry
              </button>
            </p>
          )}
          {state.status === 'loaded' && (
            <div>
              <pre className="dc-artifact-window-text">{state.text}</pre>
              {state.truncated === true && <p className="dc-empty-state">Captured content was truncated.</p>}
              {!state.isComplete && (
                <button type="button" onClick={() => loadNextWindow()}>
                  Load next window
                </button>
              )}
            </div>
          )}
        </>
      )}
    </div>
  )
}
