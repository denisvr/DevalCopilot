import { VERIFICATION_VIEW_BUDGET_BYTES } from '../agentAttemptVerification'
import type { VerificationWindowFailure, VerificationWindowState } from '../hooks/useAgentAttemptVerificationWindow'

const FAILURES: Record<VerificationWindowFailure, string> = {
  read: 'This output window could not be loaded.',
  'not-found': 'This verification evidence is no longer available.',
  invalid: 'The host answered with an output window that is not coherent for this request, so none of it is shown.',
}

const VIEW_BUDGET_KIB = VERIFICATION_VIEW_BUDGET_BYTES / 1024

interface VerificationStreamWindowViewProps {
  /** What the output is called to the owner, for example "Standard output of check 2". */
  label: string
  state: VerificationWindowState
  load: () => void
}

/**
 * One manually read window of a recorded verification stream (ADR-0052). Every load is an explicit click; the text is rendered only as
 * a plain React text node inside `<pre>`, never as HTML or Markdown, and a local view limit is disclosed as such, never as the end of
 * the capture or of the file. This component never touches browser storage, the URL or logs.
 */
export function VerificationStreamWindowView({ label, state, load }: VerificationStreamWindowViewProps) {
  return (
    <div className="dc-verification-window" aria-label={label}>
      {state.status === 'idle' && (
        <p>
          <button type="button" onClick={load}>
            Load {label.toLowerCase()}
          </button>
        </p>
      )}
      {state.status === 'loading' && (
        <div>
          <p className="dc-empty-state">Loading {label.toLowerCase()}…</p>
          {state.text !== '' && <pre className="dc-artifact-window-text">{state.text}</pre>}
        </div>
      )}
      {state.status === 'unavailable' && (
        <p className="dc-empty-state" role="status">
          This output&apos;s recorded metadata or storage could not be admitted, so it is not read.
        </p>
      )}
      {state.status === 'not-recorded' && (
        <p className="dc-empty-state" role="status">
          This output is no longer recorded for this verification run.
        </p>
      )}
      {state.status === 'missing' && (
        <p className="dc-empty-state" role="status">
          This output&apos;s sealed file could not be found, so none of it is shown.
        </p>
      )}
      {state.status === 'integrity-mismatch' && (
        <p className="dc-empty-state" role="status">
          This output&apos;s sealed file does not match its recorded length and hash, so none of it is shown.
        </p>
      )}
      {state.status === 'error' && (
        <div>
          {state.text !== '' && <pre className="dc-artifact-window-text">{state.text}</pre>}
          <p className="dc-card-evidence-error" role="status">
            {FAILURES[state.failure]}{' '}
            <button type="button" onClick={load}>
              Retry
            </button>
          </p>
        </div>
      )}
      {state.status === 'loaded' && state.totalLength === 0 && (
        <p className="dc-empty-state">This sealed output is empty (0 bytes).</p>
      )}
      {state.status === 'loaded' && state.totalLength > 0 && (
        <div>
          <pre className="dc-artifact-window-text">{state.text}</pre>
          <p className="dc-empty-state">
            {state.nextOffset} of {state.totalLength} bytes read.
            {state.isComplete ? ' This is the end of the recorded output.' : ''}
          </p>
          {state.viewLimitReached && (
            <p className="dc-card-evidence-caveat" role="status">
              This view keeps at most {VIEW_BUDGET_KIB} KiB of source bytes of one output, so the rest is not loaded here. This is a local
              view limit, not the end of the capture or of the file.
            </p>
          )}
          {!state.isComplete && !state.viewLimitReached && (
            <button type="button" onClick={load}>
              Load next window
            </button>
          )}
        </div>
      )}
    </div>
  )
}
