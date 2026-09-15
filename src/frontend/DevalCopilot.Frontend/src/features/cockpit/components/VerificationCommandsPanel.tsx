import { useState } from 'react'
import type { FormEvent } from 'react'
import { useProjectVerificationCommands } from '../hooks/useProjectVerificationCommands'

interface VerificationCommandsPanelProps {
  projectId: string
}

const DEFAULT_TIMEOUT_SECONDS = 300

/** Configuration is visible separately from source evidence. At this stage it is a durable,
 * explicit recipe only — no button here can execute a process before checkpoint-bound execution
 * and output evidence exist. */
export function VerificationCommandsPanel({ projectId }: VerificationCommandsPanelProps) {
  const { commands, loading, saving, error, configure, update, remove } = useProjectVerificationCommands(projectId)
  const [name, setName] = useState('')
  const [executablePath, setExecutablePath] = useState('')
  const [argumentsText, setArgumentsText] = useState('')
  const [timeoutSeconds, setTimeoutSeconds] = useState(DEFAULT_TIMEOUT_SECONDS)

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const created = await configure({
      name,
      executablePath,
      arguments: argumentsText.split('\n').filter(argument => argument.length > 0),
      timeoutSeconds,
      isEnabled: true,
    })
    if (created) {
      setName('')
      setExecutablePath('')
      setArgumentsText('')
      setTimeoutSeconds(DEFAULT_TIMEOUT_SECONDS)
    }
  }

  return (
    <section className="dc-verification-commands" aria-label="Verification commands">
      <div className="dc-verification-commands-heading">
        <div>
          <span className="dc-candidate-workspace-title">Verification commands</span>
          <span className="dc-workspace-evidence-subtitle">Typed local recipes; execution is added in the next slice.</span>
        </div>
      </div>

      {loading ? <span className="dc-empty-state">Loading verification commands…</span> : null}
      {!loading && commands.length === 0 ? <span className="dc-workspace-evidence-empty">No verification commands configured.</span> : null}
      {commands.map(command => (
        <div className="dc-verification-command" key={command.verificationCommandId} data-enabled={command.isEnabled === true}>
          <div>
            <strong>#{command.commandNumber} {command.name}</strong>
            <code>{command.executablePath} {command.arguments?.join(' ')}</code>
            <span>{command.timeoutSeconds}s timeout · {command.isEnabled ? 'Enabled' : 'Disabled'}</span>
          </div>
          <div className="dc-verification-command-actions">
            <button type="button" className="dc-button" disabled={saving} onClick={() => void update(command, !command.isEnabled)}>
              {command.isEnabled ? 'Disable' : 'Enable'}
            </button>
            <button type="button" className="dc-button" disabled={saving || !command.verificationCommandId} onClick={() => void remove(command.verificationCommandId!)}>
              Remove
            </button>
          </div>
        </div>
      ))}

      <form className="dc-verification-command-form" onSubmit={event => void submit(event)}>
        <input aria-label="Verification command name" required maxLength={200} value={name} onChange={event => setName(event.target.value)} placeholder="Command name" />
        <input aria-label="Verification executable path" required maxLength={1024} value={executablePath} onChange={event => setExecutablePath(event.target.value)} placeholder="Absolute executable path" />
        <textarea aria-label="Verification arguments" value={argumentsText} onChange={event => setArgumentsText(event.target.value)} placeholder="One literal argument per line" />
        <label>
          Timeout (seconds)
          <input aria-label="Verification timeout seconds" type="number" min="1" max="900" value={timeoutSeconds} onChange={event => setTimeoutSeconds(Number(event.target.value))} />
        </label>
        <button type="submit" className="dc-button" data-variant="primary" disabled={saving}>{saving ? 'Saving…' : 'Add command'}</button>
      </form>
      {error ? <span className="dc-candidate-workspace-error">{error}</span> : null}
    </section>
  )
}
