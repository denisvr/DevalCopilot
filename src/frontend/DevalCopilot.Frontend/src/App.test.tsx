import { act, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import App from './App'
import * as useSessionStatusModule from './features/cockpit/hooks/useSessionStatus'
import { OTHER_PROJECT_ID, PROJECT_ID, idleWire, wireFor } from './features/cockpit/workspaceExecutionFixture'
import { availableCapacityWire, capacityWireFor } from './features/cockpit/executionCapacityFixture'

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

describe('App GitHub stored authentication composition', () => {
  async function renderWithClient() {
    const getGitHubStoredAuthentication = vi.fn(() =>
      Promise.resolve({ host: 'github.com', context: 'StoredDefaultConfig', status: 'Authenticated', observedAtUtc: new Date('2026-10-12T09:00:00Z') }),
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
      gitHubStoredAuthenticationClient: () => ({ getGitHubStoredAuthentication }),
    }))
    const { default: FreshApp } = await import('./App')
    return { ...render(<FreshApp />), getGitHubStoredAuthentication }
  }

  it('offers one collapsed host-level region that reads nothing on mount and leaves the live cockpit unchanged', async () => {
    const { getGitHubStoredAuthentication } = await renderWithClient()

    expect(screen.getByRole('button', { name: 'Show GitHub stored authentication' })).toBeInTheDocument()
    expect(screen.getByText('cockpit run-a')).toBeInTheDocument()
    expect(getGitHubStoredAuthentication).not.toHaveBeenCalled()
  })

  it('reads only on the explicit check, keeps its answer across a project change and never reads again by itself', async () => {
    const { getGitHubStoredAuthentication } = await renderWithClient()

    fireEvent.click(screen.getByRole('button', { name: 'Show GitHub stored authentication' }))
    fireEvent.click(screen.getByRole('button', { name: 'Check stored authentication' }))
    expect(await screen.findByText('An active stored account is authenticated for github.com.')).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: /^Beta/ }))

    expect(screen.getByText('An active stored account is authenticated for github.com.')).toBeInTheDocument()
    expect(screen.getByText(/Beta has no run yet/)).toBeInTheDocument()
    expect(getGitHubStoredAuthentication).toHaveBeenCalledTimes(1)
  })
})

describe('App project GitHub origin composition', () => {
  async function renderWithClient() {
    const getProjectGitHubOrigin = vi.fn((projectId: string) =>
      Promise.resolve({
        projectId,
        source: 'RepositoryLocalConfig',
        status: 'Available',
        observedAtUtc: new Date('2026-10-12T09:00:00Z'),
        target: { host: 'github.com', owner: projectId === 'project-a' ? 'alpha' : 'beta', repository: 'widgets' },
      }),
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
      projectGitHubOriginClient: () => ({ getProjectGitHubOrigin }),
    }))
    const { default: FreshApp } = await import('./App')
    return { ...render(<FreshApp />), getProjectGitHubOrigin }
  }

  it('offers one collapsed project-level region that reads nothing on mount or on selecting another project', async () => {
    const { getProjectGitHubOrigin } = await renderWithClient()

    expect(screen.getByRole('button', { name: 'Show project GitHub origin' })).toBeInTheDocument()
    expect(screen.getByText('cockpit run-a')).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: /^Beta/ }))

    expect(screen.getByRole('button', { name: 'Show project GitHub origin' })).toBeInTheDocument()
    expect(getProjectGitHubOrigin).not.toHaveBeenCalled()
  })

  it('reads only the explicit inspection of the selected project and drops the answer when another project is selected', async () => {
    const { getProjectGitHubOrigin } = await renderWithClient()

    fireEvent.click(screen.getByRole('button', { name: 'Show project GitHub origin' }))
    fireEvent.click(screen.getByRole('button', { name: 'Inspect GitHub origin' }))
    expect(await screen.findByText('alpha')).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: /^Beta/ }))

    expect(screen.queryByText('alpha')).toBeNull()
    expect(screen.getByRole('button', { name: 'Show project GitHub origin' })).toBeInTheDocument()
    expect(getProjectGitHubOrigin).toHaveBeenCalledTimes(1)
    expect(getProjectGitHubOrigin).toHaveBeenCalledWith('project-a')
  })
})

function deferredAnswer() {
  let resolve!: (value: unknown) => void
  const promise = new Promise<unknown>((res) => {
    resolve = res
  })
  return { promise, resolve }
}

describe('App workspace execution composition (ADR-0057)', () => {
  type RegionProps = {
    projectId?: string
    runId?: string
    workspaceExecution?: { reading: { projectId: string; status: string } | null; loading: boolean; failure: string | null; refresh: () => void }
    onEvidenceRefreshRequested?: () => void
  }

  const describeState = (props: RegionProps) => {
    const state = props.workspaceExecution
    return state === undefined
      ? 'absent'
      : state.failure !== null
        ? `failed ${state.failure}`
        : state.reading !== null
          ? `${state.reading.status} for ${state.reading.projectId}`
          : state.loading
            ? 'pending'
            : 'none'
  }

  async function renderTwoProjects(read: (projectId: string) => Promise<unknown>) {
    const readWorkspaceExecutionStatus = vi.fn(read)
    vi.resetModules()
    vi.doMock('./features/cockpit/hooks/useSessionStatus', () => ({ useSessionStatus: () => 'ready' }))
    vi.doMock('./features/cockpit/hooks/useProjectSummaries', () => ({
      useProjectSummaries: () => ({
        projects: [
          { projectId: PROJECT_ID, projectName: 'Alpha', runId: 'run-a', lifecycle: 'Running', capabilities: [] },
          { projectId: OTHER_PROJECT_ID, projectName: 'Beta', runId: 'run-b', lifecycle: 'Running', capabilities: [] },
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
      CandidateWorkspacePanel: (props: RegionProps) => (
        <>
          <p>{`workspace panel ${props.projectId}: ${describeState(props)}`}</p>
          <button type="button" onClick={props.onEvidenceRefreshRequested}>
            refresh evidence
          </button>
        </>
      ),
    }))
    vi.doMock('./features/cockpit/components/RunCockpitView', () => ({
      RunCockpitView: (props: RegionProps) => <p>{`cockpit ${props.runId}: ${describeState(props)}`}</p>,
    }))
    vi.doMock('./api/clients', async (importOriginal) => ({
      ...(await importOriginal<typeof import('./api/clients')>()),
      workspaceExecutionStatusClient: () => ({ readWorkspaceExecutionStatus }),
    }))
    const { default: FreshApp } = await import('./App')
    return { ...render(<FreshApp />), readWorkspaceExecutionStatus }
  }

  it('reads the selected project once and hands the same coherent status to both regions, withheld until it is read', async () => {
    const release = deferredAnswer()
    const { readWorkspaceExecutionStatus } = await renderTwoProjects(() => release.promise)

    expect(readWorkspaceExecutionStatus).toHaveBeenCalledTimes(1)
    expect(readWorkspaceExecutionStatus).toHaveBeenCalledWith(PROJECT_ID)
    expect(screen.getByText(`workspace panel ${PROJECT_ID}: pending`)).toBeInTheDocument()
    expect(screen.getByText('cockpit run-a: pending')).toBeInTheDocument()

    await act(async () => release.resolve(idleWire(PROJECT_ID)))

    expect(await screen.findByText(`workspace panel ${PROJECT_ID}: Idle for ${PROJECT_ID}`)).toBeInTheDocument()
    expect(screen.getByText(`cockpit run-a: Idle for ${PROJECT_ID}`)).toBeInTheDocument()
    expect(readWorkspaceExecutionStatus).toHaveBeenCalledTimes(1)
  })

  it('hands a blocking status to both regions, and a failed read as a failure, never as Idle', async () => {
    const { readWorkspaceExecutionStatus, unmount } = await renderTwoProjects(() => Promise.resolve(wireFor('VerificationActive')))

    expect(await screen.findByText(`workspace panel ${PROJECT_ID}: VerificationActive for ${PROJECT_ID}`)).toBeInTheDocument()
    expect(screen.getByText(`cockpit run-a: VerificationActive for ${PROJECT_ID}`)).toBeInTheDocument()
    expect(readWorkspaceExecutionStatus).toHaveBeenCalledTimes(1)
    unmount()

    await renderTwoProjects(() => Promise.reject(new Error('down')))

    expect(await screen.findByText(`workspace panel ${PROJECT_ID}: failed read`)).toBeInTheDocument()
    expect(screen.getByText('cockpit run-a: failed read')).toBeInTheDocument()
  })

  it('refuses the answer of another project as invalid for both regions', async () => {
    await renderTwoProjects(() => Promise.resolve(idleWire(OTHER_PROJECT_ID)))

    expect(await screen.findByText(`workspace panel ${PROJECT_ID}: failed invalid`)).toBeInTheDocument()
    expect(screen.getByText('cockpit run-a: failed invalid')).toBeInTheDocument()
  })

  it('reads again for another project, never shows the previous status for it, and starts a fresh lifetime on A-B-A', async () => {
    const answers: Record<string, ReturnType<typeof deferredAnswer>[]> = {
      [PROJECT_ID]: [deferredAnswer(), deferredAnswer()],
      [OTHER_PROJECT_ID]: [deferredAnswer()],
    }
    const calls: Record<string, number> = { [PROJECT_ID]: 0, [OTHER_PROJECT_ID]: 0 }
    const { readWorkspaceExecutionStatus } = await renderTwoProjects((projectId) => answers[projectId][calls[projectId]++].promise)
    await act(async () => answers[PROJECT_ID][0].resolve(idleWire(PROJECT_ID)))
    expect(await screen.findByText(`cockpit run-a: Idle for ${PROJECT_ID}`)).toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: /^Beta/ }))

    expect(screen.getByText(`workspace panel ${OTHER_PROJECT_ID}: pending`)).toBeInTheDocument()
    expect(screen.getByText('cockpit run-b: pending')).toBeInTheDocument()
    expect(readWorkspaceExecutionStatus).toHaveBeenLastCalledWith(OTHER_PROJECT_ID)

    fireEvent.click(screen.getByRole('button', { name: /^Alpha/ }))

    expect(screen.getByText(`workspace panel ${PROJECT_ID}: pending`)).toBeInTheDocument()
    expect(screen.getByText('cockpit run-a: pending')).toBeInTheDocument()
    expect(readWorkspaceExecutionStatus).toHaveBeenCalledTimes(3)
    await act(async () => answers[OTHER_PROJECT_ID][0].resolve(idleWire(OTHER_PROJECT_ID)))
    expect(screen.getByText('cockpit run-a: pending')).toBeInTheDocument()
    await act(async () => answers[PROJECT_ID][1].resolve(wireFor('AgentAttemptActive')))
    expect(await screen.findByText(`cockpit run-a: AgentAttemptActive for ${PROJECT_ID}`)).toBeInTheDocument()
  })

  it('reads the status again on an explicit evidence refresh, which is a durable hint', async () => {
    const { readWorkspaceExecutionStatus } = await renderTwoProjects(() => Promise.resolve(idleWire(PROJECT_ID)))
    await screen.findByText(`cockpit run-a: Idle for ${PROJECT_ID}`)
    expect(readWorkspaceExecutionStatus).toHaveBeenCalledTimes(1)

    fireEvent.click(screen.getByRole('button', { name: 'refresh evidence' }))

    await waitFor(() => expect(readWorkspaceExecutionStatus).toHaveBeenCalledTimes(2))
  })
})

describe('App execution capacity composition (ADR-0058)', () => {
  type RegionProps = {
    projectId?: string
    runId?: string
    executionCapacity?: {
      reading: { status: string; recordedOwners: number | null } | null
      loading: boolean
      failure: string | null
      refresh: () => void
    }
    onEvidenceRefreshRequested?: () => void
  }

  const describeState = (props: RegionProps) => {
    const state = props.executionCapacity
    return state === undefined
      ? 'absent'
      : state.failure !== null
        ? `failed ${state.failure}`
        : state.reading !== null
          ? `${state.reading.status} with ${state.reading.recordedOwners ?? 'no count'}`
          : state.loading
            ? 'pending'
            : 'none'
  }

  async function renderApp(read: () => Promise<unknown>, sessionStatus = 'ready') {
    const readExecutionCapacity = vi.fn(read)
    vi.resetModules()
    vi.doMock('./features/cockpit/hooks/useSessionStatus', () => ({ useSessionStatus: () => sessionStatus }))
    vi.doMock('./features/cockpit/hooks/useProjectSummaries', () => ({
      useProjectSummaries: () => ({
        projects: [
          { projectId: PROJECT_ID, projectName: 'Alpha', runId: 'run-a', lifecycle: 'Running', capabilities: [] },
          { projectId: OTHER_PROJECT_ID, projectName: 'Beta', runId: 'run-b', lifecycle: 'Running', capabilities: [] },
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
      CandidateWorkspacePanel: (props: RegionProps) => (
        <>
          <p>{`workspace panel ${props.projectId}: ${describeState(props)}`}</p>
          <button type="button" onClick={props.onEvidenceRefreshRequested}>
            refresh evidence
          </button>
        </>
      ),
    }))
    vi.doMock('./features/cockpit/components/RunCockpitView', () => ({
      RunCockpitView: (props: RegionProps) => <p>{`cockpit ${props.runId}: ${describeState(props)}`}</p>,
    }))
    vi.doMock('./features/cockpit/hooks/useWorkspaceExecutionStatus', () => ({
      useWorkspaceExecutionStatus: () => ({ reading: null, loading: false, failure: null, polling: false, refresh: vi.fn() }),
    }))
    vi.doMock('./api/clients', async (importOriginal) => ({
      ...(await importOriginal<typeof import('./api/clients')>()),
      executionCapacityClient: () => ({ readExecutionCapacity }),
    }))
    const { default: FreshApp } = await import('./App')
    return { ...render(<FreshApp />), readExecutionCapacity }
  }

  it('reads the host capacity once and hands the same state to both regions, withheld until it is read', async () => {
    const release = deferredAnswer()
    const { readExecutionCapacity } = await renderApp(() => release.promise)

    expect(readExecutionCapacity).toHaveBeenCalledTimes(1)
    expect(readExecutionCapacity).toHaveBeenCalledWith()
    expect(screen.getByText(`workspace panel ${PROJECT_ID}: pending`)).toBeInTheDocument()
    expect(screen.getByText('cockpit run-a: pending')).toBeInTheDocument()

    await act(async () => release.resolve(availableCapacityWire(1)))

    expect(await screen.findByText(`workspace panel ${PROJECT_ID}: Available with 1`)).toBeInTheDocument()
    expect(screen.getByText('cockpit run-a: Available with 1')).toBeInTheDocument()
    expect(readExecutionCapacity).toHaveBeenCalledTimes(1)
  })

  it('hands a full or unavailable capacity to both regions, and a failed read as a failure, never as Available', async () => {
    const full = await renderApp(() => Promise.resolve(capacityWireFor('AtCapacity', {}, 3)))

    expect(await screen.findByText(`workspace panel ${PROJECT_ID}: AtCapacity with 3`)).toBeInTheDocument()
    expect(screen.getByText('cockpit run-a: AtCapacity with 3')).toBeInTheDocument()
    full.unmount()

    const unavailable = await renderApp(() => Promise.resolve(capacityWireFor('Unavailable')))
    expect(await screen.findByText('cockpit run-a: Unavailable with no count')).toBeInTheDocument()
    unavailable.unmount()

    await renderApp(() => Promise.reject(new Error('down')))

    expect(await screen.findByText(`workspace panel ${PROJECT_ID}: failed read`)).toBeInTheDocument()
    expect(screen.getByText('cockpit run-a: failed read')).toBeInTheDocument()
  })

  it('refuses an incoherent answer as invalid for both regions', async () => {
    await renderApp(() => Promise.resolve({ ...availableCapacityWire(), extra: true }))

    expect(await screen.findByText(`workspace panel ${PROJECT_ID}: failed invalid`)).toBeInTheDocument()
    expect(screen.getByText('cockpit run-a: failed invalid')).toBeInTheDocument()
  })

  it('owns one host-session lifetime independent of project selection: selecting another project neither re-reads nor resets it', async () => {
    const { readExecutionCapacity } = await renderApp(() => Promise.resolve(availableCapacityWire()))
    expect(await screen.findByText('cockpit run-a: Available with 0')).toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: /^Beta/ }))

    expect(screen.getByText(`workspace panel ${OTHER_PROJECT_ID}: Available with 0`)).toBeInTheDocument()
    expect(screen.getByText('cockpit run-b: Available with 0')).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: /^Alpha/ }))
    expect(screen.getByText('cockpit run-a: Available with 0')).toBeInTheDocument()
    expect(readExecutionCapacity).toHaveBeenCalledTimes(1)
  })

  it('reads the capacity again on an explicit evidence refresh, which is a durable hint', async () => {
    const { readExecutionCapacity } = await renderApp(() => Promise.resolve(availableCapacityWire()))
    await screen.findByText('cockpit run-a: Available with 0')
    expect(readExecutionCapacity).toHaveBeenCalledTimes(1)

    fireEvent.click(screen.getByRole('button', { name: 'refresh evidence' }))

    await waitFor(() => expect(readExecutionCapacity).toHaveBeenCalledTimes(2))
  })

  it('reads nothing while the session is not ready', async () => {
    const { readExecutionCapacity } = await renderApp(() => Promise.resolve(availableCapacityWire()), 'initializing')

    expect(screen.getByText(/starting the local host/i)).toBeInTheDocument()
    expect(readExecutionCapacity).not.toHaveBeenCalled()
  })
})
