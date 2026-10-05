import { omissionReasonLabel } from '../hooks/checkpointComparison'
import type { CheckpointComparison } from '../hooks/checkpointComparison'
import { useProjectGitEvidence } from '../hooks/useProjectGitEvidence'

interface WorkspaceEvidencePanelProps {
  projectId: string
  workspaceReady: boolean
  // Advanced by the project's owner to ask every checkpoint consumer to read again; `onCaptured` tells the owner a capture succeeded.
  refreshGeneration?: number
  onCaptured?: () => void
}

function abbreviate(value: string | undefined): string {
  return value ? value.slice(0, 12) : ''
}

/** What the host actually compared. "No tracked diff." is shown only for a complete capture with no tracked change: an
 * incomplete or all-omitted response never reads as clean. */
function coverageSummary(comparison: CheckpointComparison): string {
  const { trackedPathCount, comparedPathCount, omissions } = comparison
  if (comparison.complete) {
    return trackedPathCount === 0
      ? 'No tracked diff.'
      : `All ${trackedPathCount} tracked ${trackedPathCount === 1 ? 'file' : 'files'} compared.`
  }

  if (comparedPathCount === 0) {
    return `No tracked file could be compared: ${omissions.length} omitted.`
  }

  return `Partial comparison: ${comparedPathCount} of ${trackedPathCount} tracked files compared, ${omissions.length} omitted.`
}

/** Source evidence deliberately remains separate from workspace preparation: preparation
 * proves ownership; a checkpoint proves one observed content state. */
export function WorkspaceEvidencePanel({ projectId, workspaceReady, refreshGeneration, onCaptured }: WorkspaceEvidencePanelProps) {
  const { evidence, changedFiles, comparison, loading, capturing, inspecting, error, capture, inspect } = useProjectGitEvidence(
    projectId,
    workspaceReady,
    refreshGeneration,
    onCaptured,
  )

  if (!workspaceReady) {
    return null
  }

  return (
    <section className="dc-workspace-evidence" aria-label="Source evidence">
      <div className="dc-workspace-evidence-heading">
        <div>
          <span className="dc-candidate-workspace-title">Source evidence</span>
          <span className="dc-workspace-evidence-subtitle">Immutable checkpoint for the isolated workspace</span>
        </div>
        <button type="button" className="dc-button" data-variant="primary" disabled={capturing} onClick={() => void capture()}>
          {capturing ? 'Capturing…' : 'Capture checkpoint'}
        </button>
      </div>

      {loading ? <span className="dc-empty-state">Loading evidence…</span> : null}
      {!loading && !evidence?.checkpointId ? (
        <span className="dc-workspace-evidence-empty">No source checkpoint has been captured yet.</span>
      ) : null}
      {evidence?.checkpointId ? (
        <div className="dc-workspace-evidence-details">
          <span>Checkpoint #{evidence.checkpointNumber} · {evidence.changedFileCount} changed files</span>
          <code title={evidence.headCommitSha ?? undefined}>HEAD {abbreviate(evidence.headCommitSha)}</code>
          <code title={evidence.fingerprintSha256 ?? undefined}>Fingerprint {abbreviate(evidence.fingerprintSha256)}</code>
          <button type="button" className="dc-button" disabled={inspecting} onClick={() => void inspect()}>
            {inspecting ? 'Checking…' : 'Inspect files & diff'}
          </button>
        </div>
      ) : null}
      {changedFiles ? (
        <ul className="dc-workspace-evidence-files" aria-label="Changed files">
          {changedFiles.map((file) => (
            <li key={`${file.indexStatus}${file.workTreeStatus}:${file.path}`}>
              <code>{file.indexStatus}{file.workTreeStatus}</code> {file.path}
            </li>
          ))}
        </ul>
      ) : null}
      {comparison ? (
        <div className="dc-workspace-evidence-comparison" role="group" aria-label="Checkpoint comparison">
          <span role="status" data-complete={comparison.complete}>{coverageSummary(comparison)}</span>
          {comparison.omissions.length > 0 ? (
            <ul className="dc-workspace-evidence-omissions" aria-label="Files without comparison">
              {comparison.omissions.map((omission) => (
                <li key={omission.path}>
                  <code>{omission.path}</code> — {omissionReasonLabel(omission.reason)}
                </li>
              ))}
            </ul>
          ) : null}
          {comparison.text ? <pre className="dc-workspace-evidence-diff">{comparison.text}</pre> : null}
          {comparison.limitation ? <small className="dc-workspace-evidence-limitation">{comparison.limitation}</small> : null}
        </div>
      ) : null}
      {error ? <span className="dc-candidate-workspace-error">{error}</span> : null}
    </section>
  )
}
