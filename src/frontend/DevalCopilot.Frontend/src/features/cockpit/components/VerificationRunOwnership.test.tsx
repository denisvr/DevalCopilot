import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import {
  claimVerificationExecutionClient,
  projectGitEvidenceClient,
  projectVerificationCommandsClient,
  projectVerificationExecutionsClient,
} from '../../../api/clients'
import {
  ApiException,
  GetProjectGitEvidenceResponse,
  VerificationCommandResponse,
  VerificationExecutionResponse,
} from '../../../api/generated/api-client'
import { VerificationCommandsPanel } from './VerificationCommandsPanel'

// The verification panel rendered directly, WITHOUT a parent key, so every guarantee below belongs to the component's own committed
// lifetimes and not to a remount: (R1) the per-recipe control state follows the workspace identity of its execution, (R2) the
// synchronous duplicate protection belongs to the lifetime and the exact request, and (R3) the uncertainty of a start request begins
// when its unknown outcome is known and only a later explicit successful refresh discharges it.

vi.mock('../../../api/clients', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../../api/clients')>()),
  projectGitEvidenceClient: vi.fn(),
  projectVerificationCommandsClient: vi.fn(),
  projectVerificationExecutionsClient: vi.fn(),
  claimVerificationExecutionClient: vi.fn(),
}))

function deferred<T>() {
  let resolve!: (value: T) => void
  let reject!: (reason: unknown) => void
  const promise = new Promise<T>((res, rej) => {
    resolve = res
    reject = rej
  })
  return { promise, resolve, reject }
}

const workspaceOf = (projectId: string) => `${projectId}-workspace`
const UNCERTAIN = 'The verification request may or may not have been recorded. Use Refresh evidence to check the verification status before running again.'

function evidenceOf(projectId: string, workspaceId: string | null = workspaceOf(projectId)) {
  const evidence = new GetProjectGitEvidenceResponse({
    checkpointId: `${projectId}-checkpoint`,
    checkpointNumber: 1,
    changedFileCount: 0,
    headCommitSha: 'a'.repeat(40),
    fingerprintSha256: `${projectId}-fingerprint`.padEnd(64, '0'),
  })
  evidence.gitWorkspaceId = workspaceId ?? undefined
  return evidence
}

function executionOf(projectId: string, command: string, number: number, status: string, workspaceId: string | undefined) {
  const execution = new VerificationExecutionResponse({
    verificationExecutionId: `${projectId}-${command}-execution-${number}`,
    verificationCommandId: `${projectId}-${command}`,
    executionNumber: number,
    gitCheckpointId: `${projectId}-checkpoint`,
    checkpointFingerprintSha256: `${projectId}-fingerprint`.padEnd(64, '0'),
    status,
    isDispatched: status !== 'Running',
    hasStandardOutput: false,
    hasStandardError: false,
  })
  execution.gitWorkspaceId = workspaceId
  return execution
}

function commandOf(projectId: string, command: string, number: number) {
  return new VerificationCommandResponse({
    verificationCommandId: `${projectId}-${command}`,
    commandNumber: number,
    name: `${command} check`,
    executablePath: String.raw`C:\tools\verify.exe`,
    arguments: [command],
    timeoutSeconds: 300,
    isEnabled: true,
  })
}

interface Reads {
  executions: (projectId: string, call: number) => Promise<VerificationExecutionResponse[]>
  evidence: (projectId: string) => Promise<GetProjectGitEvidenceResponse>
}

function installClients(reads: Partial<Reads> = {}) {
  let executionCalls = 0
  const getProjectVerificationExecutions = vi.fn((projectId: string) => (reads.executions ?? (() => Promise.resolve([])))(projectId, ++executionCalls))
  const getProjectGitEvidence = vi.fn(reads.evidence ?? ((projectId: string) => Promise.resolve(evidenceOf(projectId))))
  const claimVerificationExecution = vi.fn().mockResolvedValue({ verificationExecutionId: 'claimed', executionNumber: 9 })
  vi.mocked(projectGitEvidenceClient).mockReturnValue({ getProjectGitEvidence } as unknown as ReturnType<typeof projectGitEvidenceClient>)
  vi.mocked(claimVerificationExecutionClient).mockReturnValue({ claimVerificationExecution } as unknown as ReturnType<typeof claimVerificationExecutionClient>)
  vi.mocked(projectVerificationCommandsClient).mockReturnValue({
    getProjectVerificationCommands: vi.fn((projectId: string) => Promise.resolve([commandOf(projectId, 'unit', 1), commandOf(projectId, 'lint', 2)])),
  } as unknown as ReturnType<typeof projectVerificationCommandsClient>)
  vi.mocked(projectVerificationExecutionsClient).mockReturnValue({ getProjectVerificationExecutions } as unknown as ReturnType<typeof projectVerificationExecutionsClient>)
  return { getProjectVerificationExecutions, getProjectGitEvidence, claimVerificationExecution }
}

const verification = () => screen.getByRole('region', { name: 'Verification commands' })
const row = (command: string) => within(verification()).getByText(`${command} check`, { exact: false }).closest('.dc-verification-command') as HTMLElement
const runButton = (command = 'unit') => within(row(command)).getByRole('button', { name: /^(Run|Pending…|Running…|Starting…)/ })

function retainedHandlerOf(button: HTMLElement): () => Promise<void> {
  const propsKey = Object.keys(button).find((key) => key.startsWith('__reactProps'))!
  return (button as unknown as Record<string, { onClick: () => Promise<void> }>)[propsKey].onClick
}

function panel(projectId: string, generation = 0) {
  return <VerificationCommandsPanel projectId={projectId} refreshGeneration={generation} />
}

async function renderSettled(projectId = 'project-a', generation = 0) {
  const view = render(panel(projectId, generation))
  await waitFor(() => expect(runButton()).toBeEnabled())
  return view
}

describe('R1: the control state of a recipe follows the workspace identity of its execution', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('keeps a historical running execution visible without disabling or labelling its own recipe or any other', async () => {
    installClients({ executions: (projectId) => Promise.resolve([executionOf(projectId, 'lint', 3, 'Running', 'old-workspace')]) })
    render(panel('project-a'))

    await waitFor(() => expect(within(verification()).getByTestId('verification-status-project-a-lint')).toHaveTextContent('Last run: Running (earlier workspace)'))
    await waitFor(() => expect(runButton('lint')).toBeEnabled())
    expect(runButton('lint')).toHaveTextContent(/^Run$/)
    expect(runButton('unit')).toBeEnabled()
    expect(within(verification()).queryByText(/A verification is running in this workspace/)).not.toBeInTheDocument()
  })

  it('does not let a historical running execution of the same recipe label its control Pending or Running', async () => {
    installClients({ executions: (projectId) => Promise.resolve([executionOf(projectId, 'unit', 3, 'Running', 'old-workspace')]) })
    render(panel('project-a'))

    await waitFor(() => expect(runButton('unit')).toBeEnabled())
    expect(runButton('unit')).toHaveTextContent(/^Run$/)
  })

  it('still lets the historical running execution be followed by a start from the enabled control', async () => {
    const clients = installClients({ executions: (projectId) => Promise.resolve([executionOf(projectId, 'lint', 3, 'Running', 'old-workspace')]) })
    render(panel('project-a'))
    await waitFor(() => expect(runButton('lint')).toBeEnabled())

    fireEvent.click(runButton('lint'))

    await waitFor(() => expect(clients.claimVerificationExecution).toHaveBeenCalledTimes(1))
    expect(clients.claimVerificationExecution.mock.calls[0][1]).toBe('project-a-lint')
  })

  it('blocks every recipe, and labels its own, for a running execution of the current workspace', async () => {
    installClients({
      executions: (projectId) => Promise.resolve([
        executionOf(projectId, 'lint', 4, 'Running', workspaceOf(projectId)),
        executionOf(projectId, 'unit', 3, 'Passed', 'old-workspace'),
      ]),
    })
    render(panel('project-a'))

    await waitFor(() => expect(within(verification()).getByText(/A verification is running in this workspace/)).toBeInTheDocument())
    expect(runButton('lint')).toBeDisabled()
    expect(runButton('lint')).toHaveTextContent('Pending…') // claimed, not yet dispatched
    expect(runButton('unit')).toBeDisabled()
    expect(runButton('unit')).toHaveTextContent(/^Run$/)
  })

  it('stays conservative when the ownership of a running execution, or of the evidence, is unknown', async () => {
    installClients({ executions: (projectId) => Promise.resolve([executionOf(projectId, 'lint', 3, 'Running', undefined)]) })
    const unknownExecution = render(panel('project-a'))
    await waitFor(() => expect(within(verification()).getByText(/A verification is running in this workspace/)).toBeInTheDocument())
    expect(runButton('unit')).toBeDisabled()
    expect(runButton('lint')).toBeDisabled()
    unknownExecution.unmount()

    installClients({
      executions: (projectId) => Promise.resolve([executionOf(projectId, 'lint', 3, 'Running', 'old-workspace')]),
      evidence: (projectId) => Promise.resolve(evidenceOf(projectId, null)),
    })
    render(panel('project-a'))
    await waitFor(() => expect(within(verification()).getByText(/A verification is running in this workspace/)).toBeInTheDocument())
    expect(runButton('unit')).toBeDisabled()
    expect(runButton('lint')).toBeDisabled()
  })
})

describe('R2: duplicate protection belongs to the committed lifetime and the exact request', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('does not let a pending request of A block B after an unkeyed replacement', async () => {
    const claimA = deferred<{ executionNumber: number }>()
    const clients = installClients()
    const view = await renderSettled('project-a')
    clients.claimVerificationExecution.mockReturnValueOnce(claimA.promise)
    fireEvent.click(runButton())
    await waitFor(() => expect(clients.claimVerificationExecution).toHaveBeenCalledTimes(1))

    view.rerender(panel('project-b'))
    await waitFor(() => expect(runButton()).toBeEnabled())
    expect(runButton()).toBeEnabled()
    fireEvent.click(runButton())

    await waitFor(() => expect(clients.claimVerificationExecution).toHaveBeenCalledTimes(2))
    expect(clients.claimVerificationExecution.mock.calls.map((call) => call[0])).toEqual(['project-a', 'project-b'])
    await act(async () => claimA.resolve({ executionNumber: 1 }))
  })

  it('lets A start again after A to B to A while its earlier request is still pending, as a new lifetime', async () => {
    const claimA = deferred<{ executionNumber: number }>()
    const clients = installClients()
    const view = await renderSettled('project-a')
    clients.claimVerificationExecution.mockReturnValueOnce(claimA.promise)
    fireEvent.click(runButton())
    await waitFor(() => expect(clients.claimVerificationExecution).toHaveBeenCalledTimes(1))

    view.rerender(panel('project-b'))
    await waitFor(() => expect(runButton()).toBeEnabled())
    view.rerender(panel('project-a'))
    await waitFor(() => expect(runButton()).toBeEnabled())
    fireEvent.click(runButton())

    await waitFor(() => expect(clients.claimVerificationExecution).toHaveBeenCalledTimes(2))
    expect(clients.claimVerificationExecution.mock.calls.map((call) => call[0])).toEqual(['project-a', 'project-a'])
    await act(async () => claimA.resolve({ executionNumber: 1 }))
  })

  it('keeps B protected against a second activation while A completes, and neither refreshes nor reports on B for A', async () => {
    const claimA = deferred<{ executionNumber: number }>()
    const claimB = deferred<{ executionNumber: number }>()
    const clients = installClients()
    const view = await renderSettled('project-a')
    clients.claimVerificationExecution.mockReturnValueOnce(claimA.promise).mockReturnValueOnce(claimB.promise)
    fireEvent.click(runButton())
    await waitFor(() => expect(clients.claimVerificationExecution).toHaveBeenCalledTimes(1))

    view.rerender(panel('project-b'))
    await waitFor(() => expect(runButton()).toBeEnabled())
    const retainedB = retainedHandlerOf(runButton())
    await act(async () => {
      void retainedB()
    })
    expect(clients.claimVerificationExecution).toHaveBeenCalledTimes(2)
    const readsBeforeAFinishes = clients.getProjectVerificationExecutions.mock.calls.length

    await act(async () => claimA.resolve({ executionNumber: 5 }))
    // A's obsolete continuation neither refreshes nor reports on B ...
    expect(clients.getProjectVerificationExecutions.mock.calls.length).toBe(readsBeforeAFinishes)
    expect(within(verification()).queryByText(/Verification #5 was requested/)).not.toBeInTheDocument()
    // ... and it did not release B's own pending protection: a retained B handler still rejects a second activation.
    await act(async () => {
      await retainedB()
      await retainedB()
    })
    expect(clients.claimVerificationExecution).toHaveBeenCalledTimes(2)

    await act(async () => claimB.resolve({ executionNumber: 6 }))
    await waitFor(() => expect(within(verification()).getByText('Verification #6 was requested.')).toBeInTheDocument())
    expect(clients.claimVerificationExecution.mock.calls.map((call) => call[0])).toEqual(['project-a', 'project-b'])
  })

  it('still prevents two synchronous activations of one lifetime and releases the protection when its own request ends', async () => {
    const claim = deferred<{ executionNumber: number }>()
    const clients = installClients()
    await renderSettled('project-a')
    clients.claimVerificationExecution.mockReturnValueOnce(claim.promise)
    const retained = retainedHandlerOf(runButton())

    await act(async () => {
      void retained()
      void retained()
    })
    expect(clients.claimVerificationExecution).toHaveBeenCalledTimes(1)

    await act(async () => claim.resolve({ executionNumber: 2 }))
    await waitFor(() => expect(within(verification()).getByText('Verification #2 was requested.')).toBeInTheDocument())
    await waitFor(() => expect(runButton()).toBeEnabled())
    fireEvent.click(runButton())
    await waitFor(() => expect(clients.claimVerificationExecution).toHaveBeenCalledTimes(2))
  })
})

describe('R3: the uncertainty boundary begins when the unknown outcome is known', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  async function startThenAdvance(options: { reads?: Partial<Reads> } = {}) {
    const claim = deferred<{ executionNumber: number }>()
    const clients = installClients(options.reads)
    const view = await renderSettled('project-a', 0)
    clients.claimVerificationExecution.mockReturnValueOnce(claim.promise)
    fireEvent.click(runButton())
    await waitFor(() => expect(clients.claimVerificationExecution).toHaveBeenCalledTimes(1))
    return { claim, clients, view }
  }

  it('is not discharged by an explicit refresh that settled while the request was pending', async () => {
    const { claim, clients, view } = await startThenAdvance()
    const reads = clients.getProjectVerificationExecutions.mock.calls.length

    view.rerender(panel('project-a', 1))
    await waitFor(() => expect(clients.getProjectVerificationExecutions.mock.calls.length).toBeGreaterThan(reads))
    await act(async () => {})
    await act(async () => claim.reject(new TypeError('network down')))

    await waitFor(() => expect(within(verification()).getByText(UNCERTAIN)).toBeInTheDocument())
    expect(runButton()).toBeDisabled()
    await new Promise((resolve) => setTimeout(resolve, 200))
    expect(runButton()).toBeDisabled()

    view.rerender(panel('project-a', 2))
    await waitFor(() => expect(runButton()).toBeEnabled())
    expect(within(verification()).queryByText(UNCERTAIN)).not.toBeInTheDocument()
    expect(clients.claimVerificationExecution).toHaveBeenCalledTimes(1)
  })

  it('is not discharged by a refresh that is still in flight when the failure arrives', async () => {
    const gate = deferred<VerificationExecutionResponse[]>()
    let gated = false
    const { claim, clients, view } = await startThenAdvance({
      reads: { executions: () => (gated ? gate.promise : Promise.resolve([])) },
    })
    gated = true
    view.rerender(panel('project-a', 1))
    await waitFor(() => expect(within(verification()).getByText('Reading verification status…')).toBeInTheDocument())

    await act(async () => claim.reject(new TypeError('network down')))
    await waitFor(() => expect(within(verification()).getByText(UNCERTAIN)).toBeInTheDocument())
    await act(async () => gate.resolve([])) // the earlier read settles successfully after the boundary
    await waitFor(() => expect(within(verification()).queryByText('Reading verification status…')).not.toBeInTheDocument())

    expect(runButton()).toBeDisabled()
    expect(within(verification()).getByText(UNCERTAIN)).toBeInTheDocument()

    gated = false
    view.rerender(panel('project-a', 2))
    await waitFor(() => expect(runButton()).toBeEnabled())
    expect(within(verification()).queryByText(UNCERTAIN)).not.toBeInTheDocument()
    expect(clients.claimVerificationExecution).toHaveBeenCalledTimes(1)
  })

  it('stays in force across a failed later refresh and is discharged by the next successful one', async () => {
    let failing = false
    const { claim, view } = await startThenAdvance({
      reads: { executions: () => (failing ? Promise.reject(new Error('unavailable')) : Promise.resolve([])) },
    })
    await act(async () => claim.reject(new TypeError('network down')))
    await waitFor(() => expect(within(verification()).getByText(UNCERTAIN)).toBeInTheDocument())

    failing = true
    view.rerender(panel('project-a', 1))
    await waitFor(() => expect(within(verification()).getByText('Verification status could not be loaded.')).toBeInTheDocument())
    expect(runButton()).toBeDisabled()
    expect(within(verification()).getByText(UNCERTAIN)).toBeInTheDocument()

    failing = false
    view.rerender(panel('project-a', 2))
    await waitFor(() => expect(runButton()).toBeEnabled())
  })

  it('does not revive once acknowledged, even while a later read is pending', async () => {
    const gate = deferred<VerificationExecutionResponse[]>()
    let gated = false
    const { claim, view } = await startThenAdvance({
      reads: { executions: () => (gated ? gate.promise : Promise.resolve([])) },
    })
    await act(async () => claim.reject(new TypeError('network down')))
    await waitFor(() => expect(within(verification()).getByText(UNCERTAIN)).toBeInTheDocument())
    view.rerender(panel('project-a', 1))
    await waitFor(() => expect(runButton()).toBeEnabled())

    gated = true
    view.rerender(panel('project-a', 2))
    await waitFor(() => expect(within(verification()).getByText('Reading verification status…')).toBeInTheDocument())

    expect(within(verification()).queryByText(UNCERTAIN)).not.toBeInTheDocument()
    await act(async () => gate.resolve([]))
    await waitFor(() => expect(runButton()).toBeEnabled())
  })

  it('belongs to the lifetime that was uncertain: another project is unaffected and a return starts fresh', async () => {
    const { claim, clients, view } = await startThenAdvance()
    await act(async () => claim.reject(new TypeError('network down')))
    await waitFor(() => expect(within(verification()).getByText(UNCERTAIN)).toBeInTheDocument())

    view.rerender(panel('project-b', 0))
    await waitFor(() => expect(runButton()).toBeEnabled())
    expect(within(verification()).queryByText(UNCERTAIN)).not.toBeInTheDocument()
    view.rerender(panel('project-a', 0))
    await waitFor(() => expect(runButton()).toBeEnabled())
    expect(within(verification()).queryByText(UNCERTAIN)).not.toBeInTheDocument()
    expect(clients.claimVerificationExecution).toHaveBeenCalledTimes(1)
  })

  it('does not record an uncertainty for the replacement when the replaced project request fails late', async () => {
    const { claim, clients, view } = await startThenAdvance()
    view.rerender(panel('project-b', 0))
    await waitFor(() => expect(runButton()).toBeEnabled())

    await act(async () => claim.reject(new TypeError('network down')))

    expect(within(verification()).queryByText(UNCERTAIN)).not.toBeInTheDocument()
    expect(runButton()).toBeEnabled()
    expect(clients.claimVerificationExecution).toHaveBeenCalledTimes(1)
  })

  it('keeps a definite refusal and an accepted claim unchanged', async () => {
    const { claim, view } = await startThenAdvance()
    view.rerender(panel('project-a', 1))
    await act(async () => claim.reject(new ApiException('conflict', 409, '{"errors":[]}', {}, null)))
    await waitFor(() => expect(within(verification()).getByText('This verification could not be started.')).toBeInTheDocument())
    expect(within(verification()).queryByText(UNCERTAIN)).not.toBeInTheDocument()
    await waitFor(() => expect(runButton()).toBeEnabled())
  })
})
