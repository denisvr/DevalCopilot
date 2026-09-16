import { useState } from 'react'
import type { FormEvent } from 'react'
import { useProjectVerificationCommands } from '../hooks/useProjectVerificationCommands'
import { useProjectGitEvidence } from '../hooks/useProjectGitEvidence'
import { useProjectVerificationExecutions } from '../hooks/useProjectVerificationExecutions'
import type { VerificationExecutionResponse } from '../../../api/clients'
import { claimVerificationExecutionClient, ClaimVerificationExecutionRequest } from '../../../api/clients'
import { VerificationExecutionOutputViewer } from './VerificationExecutionOutputViewer'

interface VerificationCommandsPanelProps {
  projectId: string
}

const DEFAULT_TIMEOUT_SECONDS = 300

function outputCaptureDescription(execution: VerificationExecutionResponse, stream: 'stdout' | 'stderr') {
  const outcome = stream === 'stdout' ? execution.standardOutputCaptureOutcome : execution.standardErrorCaptureOutcome
  if (outcome === 'RecoveredAfterHostInterruption') {
    return `${stream} recovered after host interruption · truncation unknown`
  }

  const truncated = stream === 'stdout' ? execution.standardOutputTruncated : execution.standardErrorTruncated
  return truncated ? `${stream} captured · truncated` : `${stream} captured · truncation known`
}

export function VerificationCommandsPanel({ projectId }: VerificationCommandsPanelProps) {
  const { commands, loading, saving, error, configure, update, remove } = useProjectVerificationCommands(projectId)
  const { evidence } = useProjectGitEvidence(projectId, true)
  const { executions, error: executionError, refresh: refreshExecutions } = useProjectVerificationExecutions(projectId)
  const [runningCommandId, setRunningCommandId] = useState<string | null>(null)
  const [runMessage, setRunMessage] = useState<string | null>(null)
  const [selectedOutput, setSelectedOutput] = useState<{ executionId: string; stream: 'stdout' | 'stderr' } | null>(null)
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

  async function run(commandId: string | undefined) {
    if (!commandId || !evidence?.checkpointId) {
      setRunMessage('Capture a current source checkpoint before running verification.')
      return
    }

    setRunningCommandId(commandId)
    setRunMessage(null)
    try {
      const execution = await claimVerificationExecutionClient().claimVerificationExecution(
        projectId,
        commandId,
        new ClaimVerificationExecutionRequest({ gitCheckpointId: evidence.checkpointId }),
      )
      await refreshExecutions()
      setRunMessage(`Verification #${execution.executionNumber} is pending.`)
    } catch {
      setRunMessage('This verification could not be started.')
    } finally {
      setRunningCommandId(null)
    }
  }

  return (
    <section className="dc-verification-commands" aria-label="Verification commands">
      <div className="dc-verification-commands-heading">
        <div>
          <span className="dc-candidate-workspace-title">Verification commands</span>
          <span className="dc-workspace-evidence-subtitle">Runs only from the current isolated-workspace checkpoint</span>
        </div>
      </div>

      {!evidence?.checkpointId ? <span className="dc-workspace-evidence-empty">Capture a current source checkpoint to enable Run.</span> : null}

      {loading ? <span className="dc-empty-state">Loading verification commands…</span> : null}
      {!loading && commands.length === 0 ? <span className="dc-workspace-evidence-empty">No verification commands configured.</span> : null}
      {commands.map(command => (
        (() => {
          const execution = executions.find(candidate => candidate.verificationCommandId === command.verificationCommandId)
          const isRunning = execution?.status === 'Running'
          const statusLabel = isRunning ? (execution?.isDispatched ? 'Running' : 'Pending') : execution?.status
          const canInspectOutput = execution && execution.status !== 'Running'
          const outputSelection = selectedOutput?.executionId === execution?.verificationExecutionId ? selectedOutput : null
          return (
        <div className="dc-verification-command" key={command.verificationCommandId} data-enabled={command.isEnabled === true}>
          <div>
            <strong>#{command.commandNumber} {command.name}</strong>
            <code>{command.executablePath} {command.arguments?.join(' ')}</code>
            <span>{command.timeoutSeconds}s timeout · {command.isEnabled ? 'Enabled' : 'Disabled'}</span>
            {statusLabel ? <span data-testid={`verification-status-${command.verificationCommandId}`}>Last run: {statusLabel}</span> : null}
          </div>
          <div className="dc-verification-command-actions">
            <button type="button" className="dc-button" disabled={saving} onClick={() => void update(command, !command.isEnabled)}>
              {command.isEnabled ? 'Disable' : 'Enable'}
            </button>
            <button type="button" className="dc-button" data-variant="primary" disabled={!command.isEnabled || !evidence?.checkpointId || runningCommandId !== null || isRunning} onClick={() => void run(command.verificationCommandId)}>
              {runningCommandId === command.verificationCommandId ? 'Starting…' : isRunning ? (execution?.isDispatched ? 'Running…' : 'Pending…') : 'Run'}
            </button>
            <button type="button" className="dc-button" disabled={saving || !command.verificationCommandId} onClick={() => void remove(command.verificationCommandId!)}>
              Remove
            </button>
          </div>
          {canInspectOutput ? (
            <div className="dc-verification-output-actions">
              {execution.hasStandardOutput ? (
                <>
                  <button type="button" className="dc-button" onClick={() => setSelectedOutput({ executionId: execution.verificationExecutionId!, stream: 'stdout' })}>Inspect stdout</button>
                  <span data-testid={`verification-output-status-${execution.verificationExecutionId}-stdout`}>{outputCaptureDescription(execution, 'stdout')}</span>
                </>
              ) : null}
              {execution.hasStandardError ? (
                <>
                  <button type="button" className="dc-button" onClick={() => setSelectedOutput({ executionId: execution.verificationExecutionId!, stream: 'stderr' })}>Inspect stderr</button>
                  <span data-testid={`verification-output-status-${execution.verificationExecutionId}-stderr`}>{outputCaptureDescription(execution, 'stderr')}</span>
                </>
              ) : null}
              {outputSelection ? <VerificationExecutionOutputViewer projectId={projectId} executionId={execution.verificationExecutionId!} stream={outputSelection.stream} /> : null}
            </div>
          ) : null}
        </div>
          )
        })()
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
      {executionError ? <span className="dc-candidate-workspace-error">{executionError}</span> : null}
      {runMessage ? <span className="dc-workspace-evidence-subtitle">{runMessage}</span> : null}
    </section>
  )
}
