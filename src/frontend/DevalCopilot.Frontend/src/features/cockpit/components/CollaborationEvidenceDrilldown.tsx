import { hasTrustedProcessEvidence } from '../describeProcessEvidence'
import { useCollaborationMessageEvidence } from '../hooks/useCollaborationMessageEvidence'

interface CollaborationEvidenceDrilldownProps {
  runId: string
  messageId: string
}

function formatField(label: string, value: string | number | boolean | null | undefined) {
  if (value === null || value === undefined || value === '') {
    return null
  }

  return (
    <div key={label}>
      <dt>{label}</dt>
      <dd>{String(value)}</dd>
    </div>
  )
}

/**
 * Bounded, on-demand drill-down from one collaboration card to the exact Agent Attempt that
 * produced it — never a "latest attempt" substitute; the backend resolves it strictly through the
 * message's own durable `attemptId`. Only ever rendered by the caller for a card whose
 * `provenance` is `ProviderObserved` and whose `attemptId` is non-null. Fetches using exactly this
 * component's own `runId`/`messageId` props; never infers a "current" run or attempt from
 * elsewhere in state. No raw output is ever shown — bounded metadata only, matching exactly what
 * the backend endpoint returns. Never writes fetched evidence to `localStorage`,
 * `sessionStorage`, or a URL query parameter.
 */
export function CollaborationEvidenceDrilldown({ runId, messageId }: CollaborationEvidenceDrilldownProps) {
  const { state, fetchEvidence } = useCollaborationMessageEvidence(runId, messageId)
  // The endpoint's own resolution already fails closed to `AttemptLinkBroken`/`NoAgentEvidence`
  // before this component ever sees a `success` state, but this drill-down renders `outcome`/
  // `durationMilliseconds` directly (not through `ProcessEvidenceLine`), so it reuses the exact
  // same shared trust check as that component — never a duplicated inline rule — to guard against
  // a still-running or undispatched attempt's process fields being shown as if concluded.
  const trustedProcessExecution =
    state.status === 'success' &&
    hasTrustedProcessEvidence(state.evidence.processExecution, {
      dispatched: Boolean(state.evidence.agentDispatchedAtUtc),
      running: state.evidence.attemptStatus === 'Running',
    })
      ? state.evidence.processExecution
      : null

  return (
    <details
      className="dc-card-evidence"
      onToggle={(event) => {
        if (event.currentTarget.open && state.status === 'idle') {
          fetchEvidence()
        }
      }}
    >
      <summary>Attempt evidence</summary>
      {state.status === 'idle' && null}
      {state.status === 'loading' && <p className="dc-empty-state">Loading attempt evidence…</p>}
      {state.status === 'unavailable' && <p className="dc-empty-state">This message has no linked Agent attempt.</p>}
      {state.status === 'broken' && (
        <p className="dc-empty-state" role="status">
          This message's Agent attempt evidence could not be verified and is not shown.
        </p>
      )}
      {state.status === 'error' && (
        <p className="dc-card-evidence-error" role="status">
          {state.message}{' '}
          <button type="button" onClick={() => fetchEvidence()}>
            Retry
          </button>
        </p>
      )}
      {state.status === 'success' && (
        <div className="dc-card-evidence-content">
          <p className="dc-card-evidence-caveat">
            Historical attempt evidence — reflects this attempt's own starting/result checkpoint at the time it ran,
            not a current approval and not verification against the run's current source state.
          </p>
          <dl>
            {formatField('Attempt number', state.evidence.attemptNumber ?? null)}
            {formatField('Attempt kind', state.evidence.attemptKind ?? null)}
            {formatField('Attempt status', state.evidence.attemptStatus ?? null)}
            {formatField('Agent provider', state.evidence.agentProvider ?? null)}
            {formatField('Agent role', state.evidence.agentRole ?? null)}
            {formatField('Response contract', state.evidence.agentResponseContract ?? null)}
            {formatField('Outcome', state.evidence.agentOutcome ?? null)}
            {formatField('Claimed at', state.evidence.claimedAtUtc ? String(state.evidence.claimedAtUtc) : null)}
            {formatField(
              'Dispatched at',
              state.evidence.agentDispatchedAtUtc ? String(state.evidence.agentDispatchedAtUtc) : null,
            )}
            {formatField('Completed at', state.evidence.completedAtUtc ? String(state.evidence.completedAtUtc) : null)}
            {formatField('Starting checkpoint (historical)', state.evidence.startingGitCheckpointId ?? null)}
            {formatField(
              'Starting checkpoint fingerprint (historical)',
              state.evidence.startingCheckpointFingerprintSha256 ?? null,
            )}
            {formatField('Result checkpoint (historical)', state.evidence.resultGitCheckpointId ?? null)}
            {formatField(
              'Result checkpoint fingerprint (historical)',
              state.evidence.resultCheckpointFingerprintSha256 ?? null,
            )}
            {formatField(
              'Configured timeout (historical)',
              state.evidence.processExecution?.timeoutMilliseconds != null
                ? `${state.evidence.processExecution.timeoutMilliseconds} ms`
                : null,
            )}
            {trustedProcessExecution && formatField('Process outcome (historical)', trustedProcessExecution.outcome ?? null)}
            {trustedProcessExecution &&
              formatField(
                'Process duration (historical)',
                trustedProcessExecution.durationMilliseconds != null
                  ? `${trustedProcessExecution.durationMilliseconds} ms`
                  : null,
              )}
            {state.evidence.tokenUsage &&
              formatField('Input tokens', state.evidence.tokenUsage.inputTokens ?? null)}
            {state.evidence.tokenUsage &&
              formatField('Output tokens', state.evidence.tokenUsage.outputTokens ?? null)}
          </dl>
          {state.evidence.artifacts && state.evidence.artifacts.length > 0 && (
            <>
              <p>Artifacts ({state.evidence.artifactTotalCount ?? state.evidence.artifacts.length} total):</p>
              <ul>
                {state.evidence.artifacts.map((artifact, index) => (
                  // Artifact metadata carries no durable identifier of its own (by design — see
                  // the endpoint's excluded fields); index is stable within one bounded,
                  // non-reordered response.
                  // eslint-disable-next-line react/no-array-index-key
                  <li key={index}>
                    {artifact.purpose} — {artifact.byteLength} bytes
                    {artifact.truncated === true ? ' (truncated)' : ''}
                    {artifact.truncated === null ? ' (truncation unknown)' : ''} — {artifact.captureOutcome}
                  </li>
                ))}
              </ul>
              {state.evidence.artifactsOmitted && <p className="dc-empty-state">Additional artifacts were omitted.</p>}
            </>
          )}
        </div>
      )}
    </details>
  )
}
