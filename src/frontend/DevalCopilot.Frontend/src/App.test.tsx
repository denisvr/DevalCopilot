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

  it('offers intake beside the cockpit when the only run was abandoned', async () => {
    await renderWithProject({ runId: 'run-1', lifecycle: 'Abandoned', executionMode: 'ManualAgent', canCreateRun: true })
    expect(screen.getByText('cockpit-stub')).toBeInTheDocument()
    expect(screen.getByRole('textbox', { name: 'Objective' })).toBeInTheDocument()
  })

  it('keeps an abandoned run without the availability hint blocked, since the host did not confirm coherent facts', async () => {
    await renderWithProject({ runId: 'run-1', lifecycle: 'Abandoned', executionMode: 'ManualAgent', canCreateRun: false })
    expect(screen.queryByRole('textbox', { name: 'Objective' })).toBeNull()
  })

  it('treats a project with a run and no availability hint as blocked', async () => {
    await renderWithProject({ runId: 'run-1', lifecycle: 'Completed' })
    expect(screen.queryByRole('textbox', { name: 'Objective' })).toBeNull()
  })
})

describe('App abandonment refresh', () => {
  it('hands the project list refresh to the cockpit so a recorded abandonment makes normal intake available', async () => {
    const refresh = vi.fn()
    vi.resetModules()
    vi.doMock('./features/cockpit/hooks/useSessionStatus', () => ({ useSessionStatus: () => 'ready' }))
    vi.doMock('./features/cockpit/hooks/useProjectSummaries', () => ({
      useProjectSummaries: () => ({
        projects: [{ projectId: 'project-1', projectName: 'DevalCopilot', runId: 'run-1', lifecycle: 'Running', capabilities: [] }],
        loading: false,
        error: null,
        refresh,
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
      RunCockpitView: (props: { onProjectChanged?: () => void }) => (
        <button type="button" onClick={props.onProjectChanged}>
          project changed
        </button>
      ),
    }))
    const { default: FreshApp } = await import('./App')
    render(<FreshApp />)
    refresh.mockClear()

    fireEvent.click(screen.getByRole('button', { name: 'project changed' }))

    expect(refresh).toHaveBeenCalledTimes(1)
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

describe('App run history composition', () => {
  async function renderTwoProjectsWithHistory() {
    const getProjectRunHistory = vi.fn((projectId: string) =>
      Promise.resolve({ projectId, entries: [], hasMore: false, nextBeforeExecutionNumber: undefined }),
    )
    vi.resetModules()
    vi.doMock('./features/cockpit/hooks/useSessionStatus', () => ({ useSessionStatus: () => 'ready' }))
    vi.doMock('./features/cockpit/hooks/useProjectSummaries', () => ({
      useProjectSummaries: () => ({
        projects: [
          { projectId: 'project-a', projectName: 'Alpha', runId: 'run-a', lifecycle: 'Running', canCreateRun: false, capabilities: [] },
          { projectId: 'project-b', projectName: 'Beta', capabilities: [] },
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
    vi.doMock('./features/cockpit/components/CandidateWorkspacePanel', () => ({ CandidateWorkspacePanel: () => null }))
    vi.doMock('./features/cockpit/components/RunCockpitView', () => ({
      RunCockpitView: (props: { runId: string }) => <p>{`cockpit ${props.runId}`}</p>,
    }))
    vi.doMock('./api/clients', async (importOriginal) => ({
      ...(await importOriginal<typeof import('./api/clients')>()),
      projectRunHistoryClient: () => ({ getProjectRunHistory }),
    }))
    const { default: FreshApp } = await import('./App')
    return { ...render(<FreshApp />), getProjectRunHistory }
  }

  it('offers one collapsed Run history for the selected project beside the unchanged live cockpit and requests nothing', async () => {
    const { getProjectRunHistory } = await renderTwoProjectsWithHistory()

    expect(screen.getByRole('button', { name: 'Show run history' })).toHaveAttribute('aria-expanded', 'false')
    expect(screen.getByText('cockpit run-a')).toBeInTheDocument()
    expect(screen.queryByRole('textbox', { name: 'Objective' })).toBeNull()
    expect(getProjectRunHistory).not.toHaveBeenCalled()
  })

  it('reads the selected project only when opened, leaves the live selection alone and shows another project collapsed', async () => {
    const { getProjectRunHistory } = await renderTwoProjectsWithHistory()

    fireEvent.click(screen.getByRole('button', { name: 'Show run history' }))

    expect(await screen.findByText('No runs have been recorded for this project.')).toBeInTheDocument()
    expect(getProjectRunHistory).toHaveBeenCalledTimes(1)
    expect(getProjectRunHistory).toHaveBeenCalledWith('project-a', null, 10)
    expect(screen.getByText('cockpit run-a')).toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: /^Beta/ }))

    expect(screen.getByRole('button', { name: 'Show run history' })).toHaveAttribute('aria-expanded', 'false')
    expect(screen.queryByText('No runs have been recorded for this project.')).toBeNull()
    expect(screen.getByText(/Beta has no run yet/)).toBeInTheDocument()
    expect(getProjectRunHistory).toHaveBeenCalledTimes(1)
  })
})
