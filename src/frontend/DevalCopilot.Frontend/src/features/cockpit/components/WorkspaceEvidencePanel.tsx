import { useProjectGitEvidence } from '../hooks/useProjectGitEvidence'

interface WorkspaceEvidencePanelProps {
  projectId: string
  workspaceReady: boolean
}

function abbreviate(value: string | undefined): string {
  return value ? value.slice(0, 12) : ''
}

/** Source evidence deliberately remains separate from workspace preparation: preparation
 * proves ownership; a checkpoint proves one observed content state. */
export function WorkspaceEvidencePanel({ projectId, workspaceReady }: WorkspaceEvidencePanelProps) {
  const { evidence, changedFiles, completeDiff, loading, capturing, inspecting, error, capture, inspect } = useProjectGitEvidence(
    projectId,
    workspaceReady,
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
      {completeDiff !== null ? <pre className="dc-workspace-evidence-diff">{completeDiff || 'No tracked diff.'}</pre> : null}
      {error ? <span className="dc-candidate-workspace-error">{error}</span> : null}
    </section>
  )
}
