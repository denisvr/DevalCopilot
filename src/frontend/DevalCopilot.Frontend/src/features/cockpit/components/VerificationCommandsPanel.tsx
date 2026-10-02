import type { FormEvent } from 'react'
import { useProjectVerificationCommands } from '../hooks/useProjectVerificationCommands'
import { useProjectGitEvidence } from '../hooks/useProjectGitEvidence'
import { useProjectVerificationExecutions } from '../hooks/useProjectVerificationExecutions'
import { useOwnedLifetime, useOwnedState } from '../hooks/useOwnedLifetime'
import type { VerificationExecutionResponse } from '../../../api/clients'
import { claimVerificationExecutionClient, ClaimVerificationExecutionRequest } from '../../../api/clients'
import { VerificationExecutionOutputViewer } from './VerificationExecutionOutputViewer'

interface VerificationCommandsPanelProps {
  projectId: string
}

const DEFAULT_TIMEOUT_SECONDS = 300

type OutputSelection = { executionId: string; stream: 'stdout' | 'stderr' }

// The panel's own interaction state. It belongs to the current project's lifetime: another project
// (or a return to an earlier one) starts with an empty form, no selected output and no pending
// start, whatever the previous project's handlers are still doing.
interface PanelState {
  runningCommandId: string | null
  runMessage: string | null
  selectedOutput: OutputSelection | null
  name: string
  executablePath: string
  argumentsText: string
  timeoutSeconds: number
  // Advances on every edit of the draft, even one that yields identical values, so a save can only clear the very draft it sent.
  draftVersion: number
}

function createPanelState(): PanelState {
  return {
    runningCommandId: null,
    runMessage: null,
    selectedOutput: null,
    name: '',
    executablePath: '',
    argumentsText: '',
    timeoutSeconds: DEFAULT_TIMEOUT_SECONDS,
    draftVersion: 0,
  }
}

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
  const lifetime = useOwnedLifetime(projectId)
  const [panel, commit] = useOwnedState(lifetime, createPanelState)
  const { runningCommandId, runMessage, selectedOutput, name, executablePath, argumentsText, timeoutSeconds, draftVersion } = panel

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const submittedVersion = draftVersion
    const created = await configure({
      name,
      executablePath,
      arguments: argumentsText.split('\n').filter(argument => argument.length > 0),
      timeoutSeconds,
      isEnabled: true,
    })
    // Only the draft version that was actually sent is cleared, and only for the project it was sent for.
    if (created) {
      commit(previous =>
        previous.draftVersion === submittedVersion
          ? { ...previous, name: '', executablePath: '', argumentsText: '', timeoutSeconds: DEFAULT_TIMEOUT_SECONDS, draftVersion: previous.draftVersion + 1 }
          : previous,
      )
    }
  }

  async function run(commandId: string | undefined) {
    if (!lifetime.isActive()) {
      return
    }

    if (!commandId || !evidence?.checkpointId) {
      commit(previous => ({ ...previous, runMessage: 'Capture a current source checkpoint before running verification.' }))
      return
    }

    const isCurrent = lifetime.begin('run')
    commit(previous => ({ ...previous, runningCommandId: commandId, runMessage: null }))
    try {
      const execution = await claimVerificationExecutionClient().claimVerificationExecution(
        projectId,
        commandId,
        new ClaimVerificationExecutionRequest({ gitCheckpointId: evidence.checkpointId }),
      )
      // An accepted claim stays real; only a continuation of the current project may report on it.
      if (!isCurrent()) {
        return
      }
      await refreshExecutions()
      if (isCurrent()) {
        commit(previous => ({ ...previous, runMessage: `Verification #${execution.executionNumber} is pending.` }))
      }
    } catch {
      if (isCurrent()) {
        commit(previous => ({ ...previous, runMessage: 'This verification could not be started.' }))
      }
    } finally {
      if (isCurrent()) {
        commit(previous => ({ ...previous, runningCommandId: null }))
      }
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
          const outputSelection = selectedOutput && selectedOutput.executionId === execution?.verificationExecutionId ? selectedOutput : null
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
                  <button type="button" className="dc-button" onClick={() => commit(previous => ({ ...previous, selectedOutput: { executionId: execution.verificationExecutionId!, stream: 'stdout' } }))}>Inspect stdout</button>
                  <span data-testid={`verification-output-status-${execution.verificationExecutionId}-stdout`}>{outputCaptureDescription(execution, 'stdout')}</span>
                </>
              ) : null}
              {execution.hasStandardError ? (
                <>
                  <button type="button" className="dc-button" onClick={() => commit(previous => ({ ...previous, selectedOutput: { executionId: execution.verificationExecutionId!, stream: 'stderr' } }))}>Inspect stderr</button>
                  <span data-testid={`verification-output-status-${execution.verificationExecutionId}-stderr`}>{outputCaptureDescription(execution, 'stderr')}</span>
                </>
              ) : null}
              {outputSelection ? <VerificationExecutionOutputViewer key={`${projectId}:${outputSelection.executionId}:${outputSelection.stream}`} projectId={projectId} executionId={execution.verificationExecutionId!} stream={outputSelection.stream} /> : null}
            </div>
          ) : null}
        </div>
          )
        })()
      ))}

      <form className="dc-verification-command-form" onSubmit={event => void submit(event)}>
        <input aria-label="Verification command name" required maxLength={200} value={name} onChange={event => commit(previous => ({ ...previous, name: event.target.value, draftVersion: previous.draftVersion + 1 }))} placeholder="Command name" />
        <input aria-label="Verification executable path" required maxLength={1024} value={executablePath} onChange={event => commit(previous => ({ ...previous, executablePath: event.target.value, draftVersion: previous.draftVersion + 1 }))} placeholder="Absolute executable path" />
        <textarea aria-label="Verification arguments" value={argumentsText} onChange={event => commit(previous => ({ ...previous, argumentsText: event.target.value, draftVersion: previous.draftVersion + 1 }))} placeholder="One literal argument per line" />
        <label>
          Timeout (seconds)
          <input aria-label="Verification timeout seconds" type="number" min="1" max="900" value={timeoutSeconds} onChange={event => commit(previous => ({ ...previous, timeoutSeconds: Number(event.target.value), draftVersion: previous.draftVersion + 1 }))} />
        </label>
        <button type="submit" className="dc-button" data-variant="primary" disabled={saving}>{saving ? 'Saving…' : 'Add command'}</button>
      </form>
      {error ? <span className="dc-candidate-workspace-error">{error}</span> : null}
      {executionError ? <span className="dc-candidate-workspace-error">{executionError}</span> : null}
      {runMessage ? <span className="dc-workspace-evidence-subtitle">{runMessage}</span> : null}
    </section>
  )
}
