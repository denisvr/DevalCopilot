import { useState } from 'react'
import { STARTUP_ACTION_GUIDANCE, STARTUP_FAILURE_LIMITS, type StartupFailureView } from '../../../api/startupDiagnostics'

/**
 * ADR-0072: the fixed guidance of a fatal host startup failure, shown without any API or session. It offers the closed safe
 * diagnostic to copy and nothing else: no retry, repair, reset or release action, and never raw host output.
 */
export function StartupFailureGuidance({ failure }: { failure: StartupFailureView }) {
  const [copied, setCopied] = useState<'idle' | 'copied' | 'unavailable'>('idle')

  const copy = async () => {
    try {
      await navigator.clipboard.writeText(failure.code)
      setCopied('copied')
    } catch {
      setCopied('unavailable')
    }
  }

  return (
    <section className="dc-startup-failure" aria-label="Startup failed">
      <h1>DevalCopilot could not start safely</h1>
      <p>{STARTUP_ACTION_GUIDANCE[failure.action]}</p>
      <p>{STARTUP_FAILURE_LIMITS}</p>
      <p>
        <label>
          Safe diagnostic{' '}
          <input type="text" readOnly value={failure.code} aria-label="Safe startup diagnostic" onFocus={(event) => event.target.select()} />
        </label>{' '}
        <button type="button" onClick={() => void copy()}>
          Copy diagnostic
        </button>
        {copied === 'copied' && <span role="status"> Copied.</span>}
        {copied === 'unavailable' && <span role="status"> Copying is unavailable here; select the diagnostic text instead.</span>}
      </p>
    </section>
  )
}
