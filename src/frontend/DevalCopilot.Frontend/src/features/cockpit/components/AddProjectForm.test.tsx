import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { registerProjectClient } from '../../../api/clients'
import { ApiException, RegisterProjectRequest, RegisterProjectResponse } from '../../../api/generated/api-client'
import { AddProjectForm } from './AddProjectForm'

vi.mock('../../../api/clients', () => ({
  registerProjectClient: vi.fn(),
}))

function mockClient(registerProject: ReturnType<typeof vi.fn>) {
  vi.mocked(registerProjectClient).mockReturnValue({
    registerProject,
  } as unknown as ReturnType<typeof registerProjectClient>)
}

async function openAndFill(name: string, path: string) {
  fireEvent.click(screen.getByText('Add project'))
  fireEvent.change(screen.getByLabelText('Project name'), { target: { value: name } })
  fireEvent.change(screen.getByLabelText('Repository path'), { target: { value: path } })
  fireEvent.click(screen.getByText('Register'))
}

describe('AddProjectForm', () => {
  it('is collapsed to a single button until opened', () => {
    render(<AddProjectForm onRegistered={vi.fn()} />)

    expect(screen.getByText('Add project')).toBeInTheDocument()
    expect(screen.queryByLabelText('Project name')).not.toBeInTheDocument()
  })

  it('opens the form, submits with the typed values, and calls onRegistered on success', async () => {
    const registerProject = vi.fn().mockResolvedValue(new RegisterProjectResponse({ projectId: 'project-1' }))
    mockClient(registerProject)
    const onRegistered = vi.fn()

    render(<AddProjectForm onRegistered={onRegistered} />)
    await openAndFill('DevalCopilot', String.raw`C:\repos\DevalCopilot`)

    await waitFor(() => expect(onRegistered).toHaveBeenCalledTimes(1))

    const requestArgument = registerProject.mock.calls[0][0] as RegisterProjectRequest
    expect(requestArgument.name).toBe('DevalCopilot')
    expect(requestArgument.path).toBe(String.raw`C:\repos\DevalCopilot`)

    // Collapses back to the single button once registration succeeds.
    expect(screen.getByText('Add project')).toBeInTheDocument()
    expect(screen.queryByLabelText('Project name')).not.toBeInTheDocument()
  })

  it('shows only the fixed, safe error text from a rejected registration, never a raw exception', async () => {
    const registerProject = vi.fn().mockRejectedValue(
      new ApiException(
        'Conflict',
        409,
        JSON.stringify({ errors: [{ code: 'projects.already_registered', detail: 'This path is already registered.' }] }),
        {},
        null,
      ),
    )
    mockClient(registerProject)

    render(<AddProjectForm onRegistered={vi.fn()} />)
    await openAndFill('DevalCopilot', String.raw`C:\repos\DevalCopilot`)

    expect(await screen.findByText('This path is already registered.')).toBeInTheDocument()
    // The form stays open so the user can correct their input.
    expect(screen.getByLabelText('Project name')).toBeInTheDocument()
  })

  it('falls back to a generic safe message when the error body is not the expected shape', async () => {
    const registerProject = vi.fn().mockRejectedValue(new Error('ECONNRESET at socket.js:42'))
    mockClient(registerProject)

    render(<AddProjectForm onRegistered={vi.fn()} />)
    await openAndFill('DevalCopilot', String.raw`C:\repos\DevalCopilot`)

    expect(await screen.findByText('This project could not be registered.')).toBeInTheDocument()
    expect(screen.queryByText(/ECONNRESET/)).not.toBeInTheDocument()
  })
})
