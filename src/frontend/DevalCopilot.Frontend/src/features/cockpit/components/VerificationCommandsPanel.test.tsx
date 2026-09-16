import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import {
  claimVerificationExecutionClient,
  configureVerificationCommandClient,
  deleteVerificationCommandClient,
  projectGitEvidenceClient,
  projectVerificationCommandsClient,
  projectVerificationExecutionsClient,
  updateVerificationCommandClient,
} from '../../../api/clients'
import { ClaimVerificationExecutionRequest, ConfigureVerificationCommandResponse, GetProjectGitEvidenceResponse, VerificationCommandResponse, VerificationExecutionResponse } from '../../../api/generated/api-client'
import { VerificationCommandsPanel } from './VerificationCommandsPanel'

vi.mock('../../../api/clients', async () => {
  const actual = await vi.importActual<typeof import('../../../api/clients')>('../../../api/clients')
  return {
    ...actual,
    claimVerificationExecutionClient: vi.fn(),
    configureVerificationCommandClient: vi.fn(),
    deleteVerificationCommandClient: vi.fn(),
    projectGitEvidenceClient: vi.fn(),
    projectVerificationCommandsClient: vi.fn(),
    projectVerificationExecutionsClient: vi.fn(),
    updateVerificationCommandClient: vi.fn(),
  }
})

describe('VerificationCommandsPanel', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('sends each entered argument as a literal array element and refreshes the durable configuration', async () => {
    const getProjectVerificationCommands = vi.fn()
      .mockResolvedValueOnce([])
      .mockResolvedValueOnce([
        new VerificationCommandResponse({
          verificationCommandId: 'command-1',
          commandNumber: 1,
          name: 'Backend tests',
          executablePath: String.raw`C:\Program Files\dotnet\dotnet.exe`,
          arguments: ['test', '--no-restore'],
          timeoutSeconds: 300,
          isEnabled: true,
        }),
      ])
    const configureVerificationCommand = vi.fn().mockResolvedValue(
      new ConfigureVerificationCommandResponse({ verificationCommandId: 'command-1', commandNumber: 1 }),
    )
    vi.mocked(projectVerificationCommandsClient).mockReturnValue({
      getProjectVerificationCommands,
    } as unknown as ReturnType<typeof projectVerificationCommandsClient>)
    vi.mocked(configureVerificationCommandClient).mockReturnValue({
      configureVerificationCommand,
    } as unknown as ReturnType<typeof configureVerificationCommandClient>)
    vi.mocked(updateVerificationCommandClient).mockReturnValue({
      updateVerificationCommand: vi.fn(),
    } as unknown as ReturnType<typeof updateVerificationCommandClient>)
    vi.mocked(deleteVerificationCommandClient).mockReturnValue({
      deleteVerificationCommand: vi.fn(),
    } as unknown as ReturnType<typeof deleteVerificationCommandClient>)

    render(<VerificationCommandsPanel projectId="project-1" />)

    await screen.findByText('No verification commands configured.')
    fireEvent.change(screen.getByLabelText('Verification command name'), { target: { value: 'Backend tests' } })
    fireEvent.change(screen.getByLabelText('Verification executable path'), { target: { value: String.raw`C:\Program Files\dotnet\dotnet.exe` } })
    fireEvent.change(screen.getByLabelText('Verification arguments'), { target: { value: 'test\n--no-restore' } })
    fireEvent.click(screen.getByText('Add command'))

    await waitFor(() => expect(configureVerificationCommand).toHaveBeenCalledWith(
      'project-1',
      expect.objectContaining({ arguments: ['test', '--no-restore'], timeoutSeconds: 300 }),
    ))
    expect(await screen.findByText('#1 Backend tests')).toBeInTheDocument()
    expect(screen.getByText('Runs only from the current isolated-workspace checkpoint')).toBeInTheDocument()
  })

  it('runs only with a current checkpoint and sends the generated request class', async () => {
    const command = new VerificationCommandResponse({
      verificationCommandId: 'command-1',
      commandNumber: 1,
      name: 'Backend tests',
      executablePath: String.raw`C:\Program Files\dotnet\dotnet.exe`,
      arguments: ['test'],
      timeoutSeconds: 300,
      isEnabled: true,
    })
    vi.mocked(projectVerificationCommandsClient).mockReturnValue({
      getProjectVerificationCommands: vi.fn().mockResolvedValue([command]),
    } as unknown as ReturnType<typeof projectVerificationCommandsClient>)
    vi.mocked(projectGitEvidenceClient).mockReturnValue({
      getProjectGitEvidence: vi.fn().mockResolvedValue(new GetProjectGitEvidenceResponse({ checkpointId: 'checkpoint-1' })),
    } as unknown as ReturnType<typeof projectGitEvidenceClient>)
    vi.mocked(projectVerificationExecutionsClient).mockReturnValue({
      getProjectVerificationExecutions: vi.fn().mockResolvedValue([
        new VerificationExecutionResponse({
          verificationExecutionId: 'execution-1',
          verificationCommandId: 'command-1',
          executionNumber: 1,
          status: 'Passed',
          isDispatched: true,
          hasStandardOutput: false,
          hasStandardError: false,
        }),
      ]),
    } as unknown as ReturnType<typeof projectVerificationExecutionsClient>)
    const claimVerificationExecution = vi.fn().mockResolvedValue({ executionNumber: 2 })
    vi.mocked(claimVerificationExecutionClient).mockReturnValue({ claimVerificationExecution } as unknown as ReturnType<typeof claimVerificationExecutionClient>)

    render(<VerificationCommandsPanel projectId="project-1" />)

    const runButton = await screen.findByText('Run')
    expect(runButton).toBeEnabled()
    fireEvent.click(runButton)

    await waitFor(() => expect(claimVerificationExecution).toHaveBeenCalledWith(
      'project-1',
      'command-1',
      expect.any(ClaimVerificationExecutionRequest),
    ))
    expect(claimVerificationExecution.mock.calls[0][2].gitCheckpointId).toBe('checkpoint-1')
  })

  it('labels recovered output as host-interrupted with unknown truncation', async () => {
    const command = new VerificationCommandResponse({
      verificationCommandId: 'command-1',
      commandNumber: 1,
      name: 'Backend tests',
      executablePath: String.raw`C:\Program Files\dotnet\dotnet.exe`,
      arguments: ['test'],
      timeoutSeconds: 300,
      isEnabled: true,
    })
    vi.mocked(projectVerificationCommandsClient).mockReturnValue({
      getProjectVerificationCommands: vi.fn().mockResolvedValue([command]),
    } as unknown as ReturnType<typeof projectVerificationCommandsClient>)
    vi.mocked(projectGitEvidenceClient).mockReturnValue({
      getProjectGitEvidence: vi.fn().mockResolvedValue(new GetProjectGitEvidenceResponse({ checkpointId: 'checkpoint-1' })),
    } as unknown as ReturnType<typeof projectGitEvidenceClient>)
    vi.mocked(projectVerificationExecutionsClient).mockReturnValue({
      getProjectVerificationExecutions: vi.fn().mockResolvedValue([
        new VerificationExecutionResponse({
          verificationExecutionId: 'execution-1',
          verificationCommandId: 'command-1',
          executionNumber: 1,
          status: 'Interrupted',
          isDispatched: true,
          hasStandardOutput: true,
          hasStandardError: false,
          standardOutputTruncated: undefined,
          standardOutputCaptureOutcome: 'RecoveredAfterHostInterruption',
        }),
      ]),
    } as unknown as ReturnType<typeof projectVerificationExecutionsClient>)

    render(<VerificationCommandsPanel projectId="project-1" />)

    expect(await screen.findByText('stdout recovered after host interruption · truncation unknown')).toBeInTheDocument()
  })
})
