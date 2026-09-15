import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import {
  configureVerificationCommandClient,
  deleteVerificationCommandClient,
  projectVerificationCommandsClient,
  updateVerificationCommandClient,
} from '../../../api/clients'
import { ConfigureVerificationCommandResponse, VerificationCommandResponse } from '../../../api/generated/api-client'
import { VerificationCommandsPanel } from './VerificationCommandsPanel'

vi.mock('../../../api/clients', () => ({
  configureVerificationCommandClient: vi.fn(),
  deleteVerificationCommandClient: vi.fn(),
  projectVerificationCommandsClient: vi.fn(),
  updateVerificationCommandClient: vi.fn(),
}))

describe('VerificationCommandsPanel', () => {
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
    expect(screen.getByText('Typed local recipes; execution is added in the next slice.')).toBeInTheDocument()
  })
})
