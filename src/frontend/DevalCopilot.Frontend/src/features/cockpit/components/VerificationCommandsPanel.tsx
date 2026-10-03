import { useEffect, useLayoutEffect, useRef } from 'react'
import type { FormEvent } from 'react'
import { useProjectVerificationCommands } from '../hooks/useProjectVerificationCommands'
import { useProjectGitEvidence } from '../hooks/useProjectGitEvidence'
import { useProjectVerificationExecutions } from '../hooks/useProjectVerificationExecutions'
import { useOwnedLifetime, useOwnedState } from '../hooks/useOwnedLifetime'
import type { OwnedLifetime } from '../hooks/useOwnedLifetime'
import type { VerificationExecutionResponse } from '../../../api/clients'
import { claimVerificationExecutionClient, ClaimVerificationExecutionRequest } from '../../../api/clients'
import { ApiException } from '../../../api/generated/api-client'
import { VerificationExecutionOutputViewer } from './VerificationExecutionOutputViewer'

interface VerificationCommandsPanelProps {
  projectId: string
  // Advanced by the project's owner when the current checkpoint metadata must be read again.
  refreshGeneration?: number
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
  // The refresh generation that was current when a start request ended without a definite answer (a transport failure or a server
  // fault), i.e. when the unknown outcome became known: the host may or may not have recorded it. Reads begun at or before that
  // generation cannot discharge it; another local submission needs a later explicit refresh that read the status successfully.
  uncertainAtGeneration: number | null
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
    uncertainAtGeneration: null,
  }
}

const UNCERTAIN_START_MESSAGE =
  'The verification request may or may not have been recorded. Use Refresh evidence to check the verification status before running again.'

// A definite refusal is a client or conflict answer of the host; anything else (no answer, a server fault, an unreadable answer)
// leaves the outcome of the request unknown and is never reported as a refusal or as an acceptance.
function isDefiniteRefusal(caught: unknown): boolean {
  return ApiException.isApiException(caught) && caught.status >= 400 && caught.status < 500
}

function outputCaptureDescription(execution: VerificationExecutionResponse, stream: 'stdout' | 'stderr') {
  const outcome = stream === 'stdout' ? execution.standardOutputCaptureOutcome : execution.standardErrorCaptureOutcome
  if (outcome === 'RecoveredAfterHostInterruption') {
    return `${stream} recovered after host interruption · truncation unknown`
  }

  const truncated = stream === 'stdout' ? execution.standardOutputTruncated : execution.standardErrorTruncated
  return truncated ? `${stream} captured · truncated` : `${stream} captured · truncation known`
}

export function VerificationCommandsPanel({ projectId, refreshGeneration }: VerificationCommandsPanelProps) {
  const { commands, loading, saving, error, configure, update, remove } = useProjectVerificationCommands(projectId)
  const { evidence, current: evidenceCurrent, loading: evidenceLoading } = useProjectGitEvidence(projectId, true, refreshGeneration)
  const {
    executions,
    error: executionError,
    refresh: refreshExecutions,
    current: executionsCurrent,
    loading: executionsLoading,
    readFailed: executionsFailed,
  } = useProjectVerificationExecutions(projectId, refreshGeneration)
  const lifetime = useOwnedLifetime(projectId)
  const [panel, commit] = useOwnedState(lifetime, createPanelState)
  const { runningCommandId, runMessage, selectedOutput, name, executablePath, argumentsText, timeoutSeconds, draftVersion, uncertainAtGeneration } = panel

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

  // Run needs a checkpoint whose current metadata was read, a settled successful read of the verification executions of the displayed
  // generation, and no running verification in the workspace that checkpoint belongs to (whatever its recipe). An execution of another
  // workspace never blocks, and the answer of a start request that may have been recorded blocks until a later refresh read the status.
  const checkpointId = evidence?.checkpointId
  // Ownership is conservative: an execution or evidence without a workspace identity is treated as the current workspace's. Only a
  // confirmed different workspace is historical, and a historical fact is displayed but never controls this workspace's Run.
  const inCurrentWorkspace = (execution: VerificationExecutionResponse) =>
    execution.gitWorkspaceId === undefined || evidence?.gitWorkspaceId === undefined || execution.gitWorkspaceId === evidence.gitWorkspaceId
  const workspaceRunning = executions.some(execution => execution.status === 'Running' && inCurrentWorkspace(execution))
  const executionsReading = !executionsCurrent && (executionsLoading || !executionsFailed)
  const uncertaintyDischarged =
    uncertainAtGeneration !== null && (refreshGeneration ?? 0) > uncertainAtGeneration && executionsCurrent && evidenceCurrent
  const startUncertain = uncertainAtGeneration !== null && !uncertaintyDischarged
  const canRun = Boolean(checkpointId) && evidenceCurrent && executionsCurrent && !workspaceRunning && !startUncertain

  // Handlers are rebound on every render, but one retained from an earlier render must decide from the newest authority.
  const latest = useRef({ canRun, checkpointId, generation: refreshGeneration ?? 0 })
  useLayoutEffect(() => {
    latest.current = { canRun, checkpointId, generation: refreshGeneration ?? 0 }
  })
  // A later explicit refresh that read the status acknowledges the uncertainty for good: it is cleared from the lifetime's state, so a
  // later pending or failed read can never revive it.
  useEffect(() => {
    if (uncertaintyDischarged) {
      commit(previous => (previous.uncertainAtGeneration === uncertainAtGeneration ? { ...previous, uncertainAtGeneration: null } : previous))
    }
  }, [uncertaintyDischarged, uncertainAtGeneration, commit])

  // The request in flight, owned by the committed lifetime that sent it: another lifetime's request neither blocks this one nor
  // releases its protection.
  const starting = useRef<{ lifetime: OwnedLifetime } | null>(null)

  async function run(commandId: string | undefined) {
    if (!lifetime.isActive() || starting.current?.lifetime === lifetime) {
      return
    }

    const authority = latest.current
    if (!commandId || !authority.checkpointId || !authority.canRun) {
      commit(previous => ({ ...previous, runMessage: 'Capture or refresh a current source checkpoint, and wait for no running verification, before running verification.' }))
      return
    }

    const mine = { lifetime }
    starting.current = mine
    const isCurrent = lifetime.begin('run')
    commit(previous => ({ ...previous, runningCommandId: commandId, runMessage: null }))
    try {
      const execution = await claimVerificationExecutionClient().claimVerificationExecution(
        projectId,
        commandId,
        new ClaimVerificationExecutionRequest({ gitCheckpointId: authority.checkpointId }),
      )
      // An accepted claim stays real; only a continuation of the current project may report on it.
      if (!isCurrent()) {
        return
      }
      await refreshExecutions()
      if (isCurrent()) {
        commit(previous => ({ ...previous, runMessage: `Verification #${execution.executionNumber} was requested.` }))
      }
    } catch (caught) {
      if (isCurrent()) {
        commit(previous => isDefiniteRefusal(caught)
          ? { ...previous, runMessage: 'This verification could not be started.' }
          : { ...previous, runMessage: null, uncertainAtGeneration: latest.current.generation })
      }
    } finally {
      if (starting.current === mine) {
        starting.current = null
      }
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

      {!evidenceCurrent ? (
        <span className="dc-workspace-evidence-empty">
          {evidenceLoading ? 'Reading the current source checkpoint…' : 'The current source checkpoint could not be refreshed. Use Refresh evidence to enable Run.'}
        </span>
      ) : null}
      {evidenceCurrent && !evidence?.checkpointId ? <span className="dc-workspace-evidence-empty">Capture a current source checkpoint to enable Run.</span> : null}
      {executionsReading ? <span className="dc-workspace-evidence-empty">Reading verification status…</span> : null}
      {executionsCurrent && workspaceRunning ? <span className="dc-workspace-evidence-empty">A verification is running in this workspace; Run is available once it finishes.</span> : null}
      {startUncertain ? <span className="dc-candidate-workspace-attention">{UNCERTAIN_START_MESSAGE}</span> : null}

      {loading ? <span className="dc-empty-state">Loading verification commands…</span> : null}
      {!loading && commands.length === 0 ? <span className="dc-workspace-evidence-empty">No verification commands configured.</span> : null}
      {commands.map(command => (
        (() => {
          const execution = executions.find(candidate => candidate.verificationCommandId === command.verificationCommandId)
          // Display and control state are separate: only an execution of the current workspace makes this recipe Pending or Running.
          const historical = execution !== undefined && !inCurrentWorkspace(execution)
          const isRunning = execution?.status === 'Running' && !historical
          const statusLabel = isRunning
            ? (execution?.isDispatched ? 'Running' : 'Pending')
            : historical && execution?.status === 'Running' ? 'Running (earlier workspace)' : execution?.status
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
            <button type="button" className="dc-button" data-variant="primary" disabled={!command.isEnabled || !canRun || runningCommandId !== null || isRunning} onClick={() => void run(command.verificationCommandId)}>
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
