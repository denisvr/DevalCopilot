import { fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import App from './App'
import * as useSessionStatusModule from './features/cockpit/hooks/useSessionStatus'

vi.mock('./features/cockpit/hooks/useSessionStatus')

const useSessionStatusMock = vi.mocked(useSessionStatusModule.useSessionStatus)

describe('App session states', () => {
  it('shows a starting state while the session is not yet resolved', () => {
    useSessionStatusMock.mockReturnValue('initializing')

    render(<App />)

    expect(screen.getByText(/starting the local host/i)).toBeInTheDocument()
  })

  it('shows a sidecar-failed state without ever showing the run cockpit', () => {
    useSessionStatusMock.mockReturnValue('failed')

    render(<App />)

    expect(screen.getByText(/no launch session is available/i)).toBeInTheDocument()
  })

  it('shows a distinct disconnected/recovery state, directing the user to reopen the app', () => {
    useSessionStatusMock.mockReturnValue('disconnected')

    render(<App />)

    expect(screen.getByText(/disconnected/i)).toBeInTheDocument()
    expect(screen.getByText(/close and reopen/i)).toBeInTheDocument()
  })
})

describe('App run intake availability', () => {
  async function renderWithProject(project: Record<string, unknown>) {
    vi.resetModules()
    vi.doMock('./features/cockpit/hooks/useSessionStatus', () => ({ useSessionStatus: () => 'ready' }))
    vi.doMock('./features/cockpit/hooks/useProjectSummaries', () => ({
      useProjectSummaries: () => ({
        projects: [{ projectId: 'project-1', projectName: 'DevalCopilot', capabilities: [], ...project }],
        loading: false,
        error: null,
        refresh: vi.fn(),
      }),
    }))
    vi.doMock('./features/cockpit/hooks/useProviderRuntimePreflight', () => ({
      useProviderRuntimePreflight: () => ({ providers: [], loading: false, error: null, refresh: vi.fn() }),
    }))
    vi.doMock('./features/cockpit/hooks/useHostCapabilityRefresh', () => ({
      useHostCapabilityRefresh: () => ({ refreshingCapability: null, requestRefresh: vi.fn() }),
    }))
    vi.doMock('./features/cockpit/components/CandidateWorkspacePanel', () => ({ CandidateWorkspacePanel: () => null }))
    vi.doMock('./features/cockpit/components/RunCockpitView', () => ({
      RunCockpitView: () => <p>cockpit-stub</p>,
    }))
    const { default: FreshApp } = await import('./App')
    return render(<FreshApp />)
  }

  it('offers intake and the labelled demo for a project with no run', async () => {
    await renderWithProject({})
    expect(screen.getByRole('textbox', { name: 'Objective' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Start simulated run' })).toBeInTheDocument()
  })

  it('offers intake beside the cockpit when every run is terminal', async () => {
    await renderWithProject({ runId: 'run-1', lifecycle: 'Completed', executionMode: 'ManualAgent', canCreateRun: true })
    expect(screen.getByText('cockpit-stub')).toBeInTheDocument()
    expect(screen.getByRole('textbox', { name: 'Objective' })).toBeInTheDocument()
  })

  it('shows only the blocked reason while the project has an unfinished run', async () => {
    await renderWithProject({ runId: 'run-1', lifecycle: 'Running', executionMode: 'ManualAgent', canCreateRun: false })
    expect(screen.getByText(/only after every run of this project has finished/i)).toBeInTheDocument()
    expect(screen.queryByRole('textbox', { name: 'Objective' })).toBeNull()
    expect(screen.queryByRole('button', { name: 'Start simulated run' })).toBeNull()
  })

  it('treats a project with a run and no availability hint as blocked', async () => {
    await renderWithProject({ runId: 'run-1', lifecycle: 'Completed' })
    expect(screen.queryByRole('textbox', { name: 'Objective' })).toBeNull()
  })
})

describe('App evidence refresh connection', () => {
  async function renderTwoProjects() {
    vi.resetModules()
    vi.doMock('./features/cockpit/hooks/useSessionStatus', () => ({ useSessionStatus: () => 'ready' }))
    vi.doMock('./features/cockpit/hooks/useProjectSummaries', () => ({
      useProjectSummaries: () => ({
        projects: [
          { projectId: 'project-a', projectName: 'Alpha', runId: 'run-a', lifecycle: 'Running', capabilities: [] },
          { projectId: 'project-b', projectName: 'Beta', runId: 'run-b', lifecycle: 'Running', capabilities: [] },
        ],
        loading: false,
        error: null,
        refresh: vi.fn(),
      }),
    }))
    vi.doMock('./features/cockpit/hooks/useProviderRuntimePreflight', () => ({
      useProviderRuntimePreflight: () => ({ providers: [], loading: false, error: null, refresh: vi.fn() }),
    }))
    vi.doMock('./features/cockpit/hooks/useHostCapabilityRefresh', () => ({
      useHostCapabilityRefresh: () => ({ refreshingCapability: null, requestRefresh: vi.fn() }),
    }))
    vi.doMock('./features/cockpit/components/CandidateWorkspacePanel', () => ({
      CandidateWorkspacePanel: (props: { projectId: string; onEvidenceRefreshRequested?: () => void }) => (
        <button type="button" onClick={props.onEvidenceRefreshRequested}>{`refresh ${props.projectId}`}</button>
      ),
    }))
    vi.doMock('./features/cockpit/components/RunCockpitView', () => ({
      RunCockpitView: (props: { runId: string; evidenceRefreshGeneration?: number }) => (
        <p>{`cockpit ${props.runId} generation ${props.evidenceRefreshGeneration}`}</p>
      ),
    }))
    const { default: FreshApp } = await import('./App')
    return render(<FreshApp />)
  }

  it('connects the workspace action to the selected project\'s run, and gives every project and run its own generation', async () => {
    await renderTwoProjects()
    expect(screen.getByText('cockpit run-a generation 0')).toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: 'refresh project-a' }))
    fireEvent.click(screen.getByRole('button', { name: 'refresh project-a' }))
    expect(screen.getByText('cockpit run-a generation 2')).toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: /^Beta/ }))
    expect(screen.getByText('cockpit run-b generation 0')).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: 'refresh project-b' }))
    expect(screen.getByText('cockpit run-b generation 1')).toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: /^Alpha/ }))
    expect(screen.getByText('cockpit run-a generation 0')).toBeInTheDocument()
  })
})
