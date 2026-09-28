import { useState } from 'react'
import type { AgentAttemptArtifactMetadataResponse } from '../../../api/clients'
import { useSealedAgentArtifactWindow } from '../hooks/useSealedAgentArtifactWindow'

interface AttemptArtifactWindowViewerProps {
  runId: string
  messageId: string
  /** This attempt's own bounded artifact metadata, exactly as the evidence drill-down already
   * loaded it — purpose selection is offered only for a purpose actually present here, never for
   * every allowlisted purpose unconditionally. */
  artifacts: readonly AgentAttemptArtifactMetadataResponse[]
}

const PURPOSE_LABELS: Record<string, string> = {
  AgentContextManifest: 'Context manifest',
  AgentStandardOutput: 'Standard output',
  AgentStandardError: 'Standard error',
  AgentFinalResponse: 'Final response',
}

/**
 * The two truthful caveats this viewer can make, matched to how each purpose is actually
 * produced and recorded (`Artifact.Sensitivity`, set at the point each purpose is written — see
 * `CreateCodexPlanningAttemptCommandHandler` and its sibling claim handlers for
 * `AgentContextManifest`'s `HostConstructedContent`, and `RecordAgentAttemptResultCommandHandler`
 * for the other three purposes' `RedactedBestEffort`). This branches on the already-selected,
 * closed-allowlist purpose string the component already holds — it is not a new sensitivity API
 * or wire field, and no schema changed to support it.
 */
function sensitivityCaveatFor(purpose: string): string {
  if (purpose === 'AgentContextManifest') {
    return (
      "Historical text composed entirely by DevalCopilot's own application code from " +
      'already-curated durable fields — never raw, unexamined provider output. This reduces but ' +
      'does not eliminate the chance it names something sensitive (for example a file path or ' +
      'identifier from this run); it is not a live view of current source state.'
    )
  }

  return (
    'Historical raw provider text, passed through a fixed, documented best-effort redaction ' +
    'pattern list before capture — never a guarantee. An unrecognized or unusual secret format ' +
    'may still remain in it; it is not a live tail of a running attempt.'
  )
}

/**
 * On-demand viewer for one bounded, integrity-verified text window of a sealed Agent-attempt
 * artifact — an extension of the existing collaboration evidence drill-down. Offers purpose
 * selection and manual next-window loading only for artifact metadata actually linked to this
 * attempt (see `artifacts`). Fetched text is always rendered as plain text (never HTML), and this
 * component never writes it to `localStorage`/`sessionStorage`/the URL.
 */
export function AttemptArtifactWindowViewer({ runId, messageId, artifacts }: AttemptArtifactWindowViewerProps) {
  const availablePurposes = artifacts
    .map((artifact) => artifact.purpose)
    .filter((purpose): purpose is string => Boolean(purpose) && purpose! in PURPOSE_LABELS)

  const [selectedPurpose, setSelectedPurpose] = useState<string | null>(null)
  const { state, loadNextWindow } = useSealedAgentArtifactWindow(runId, messageId, selectedPurpose)

  if (availablePurposes.length === 0) {
    return null
  }

  return (
    <div className="dc-artifact-window-viewer">
      <label>
        Artifact:{' '}
        <select
          value={selectedPurpose ?? ''}
          onChange={(event) => setSelectedPurpose(event.target.value === '' ? null : event.target.value)}
        >
          <option value="">Select an artifact…</option>
          {availablePurposes.map((purpose) => (
            <option key={purpose} value={purpose}>
              {PURPOSE_LABELS[purpose]}
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
