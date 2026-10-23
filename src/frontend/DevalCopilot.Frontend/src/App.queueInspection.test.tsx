import { act, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { OTHER_PROJECT_ID, PROJECT_ID } from './features/cockpit/workspaceExecutionFixture'
import { availableCapacityWire } from './features/cockpit/executionCapacityFixture'
import { headerFor, recordedIndexFor, stepFor } from './features/cockpit/localExecutionInspectionFixture'
import { recordedQueueFor, runIdFor, waitingEntryFor } from './features/cockpit/localExecutionQueueInspectionFixture'

// The global outstanding queue admission region (ADR-0064) belongs to the authenticated host session, not to the selected project: it is
// mounted beside the global recorded-execution limit, reads only on its own explicit actions and is neither owned, refreshed nor reset
// by project selection, evidence refresh or time. Everything below it is stubbed so only the region's reads are observed.

interface Options {
  sessionStatus?: () => string
  noProjects?: boolean
}

async function renderApp(options: Options = {}) {
  const readOutstanding = vi.fn(() => Promise.resolve(recordedQueueFor([waitingEntryFor(1), waitingEntryFor(2)])))
  const readIndex = vi.fn((runId: string) =>
    Promise.resolve(
      recordedIndexFor(runId, [stepFor(0)], {
        sequence: headerFor({ admissionMode: 'WaitForAdmission', recordedState: 'WaitingForAdmission', reasonCode: null, endedAtUtc: null }),
      }),
    ),
  )
  const readExecutionCapacity = vi.fn(() => Promise.resolve(availableCapacityWire()))
  vi.resetModules()
  vi.doMock('./features/cockpit/hooks/useSessionStatus', () => ({ useSessionStatus: options.sessionStatus ?? (() => 'ready') }))
  vi.doMock('./features/cockpit/hooks/useProjectSummaries', () => ({
    useProjectSummaries: () => ({
      projects: options.noProjects
        ? []
        : [
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
    CandidateWorkspacePanel: (props: { projectId: string; onEvidenceRefreshRequested: () => void }) => (
      <>
        <p>{`workspace panel ${props.projectId}`}</p>
        <button type="button" onClick={props.onEvidenceRefreshRequested}>
          refresh evidence
        </button>
      </>
    ),
  }))
  vi.doMock('./features/cockpit/components/RunCockpitView', () => ({
    RunCockpitView: (props: { runId: string }) => <p>{`cockpit ${props.runId}`}</p>,
  }))
  vi.doMock('./features/cockpit/hooks/useWorkspaceExecutionStatus', () => ({
    useWorkspaceExecutionStatus: () => ({ reading: null, loading: false, failure: null, polling: false, refresh: vi.fn() }),
  }))
  vi.doMock('./api/clients', async (importOriginal) => ({
    ...(await importOriginal<typeof import('./api/clients')>()),
    executionCapacityClient: () => ({ readExecutionCapacity }),
    localExecutionQueueInspectionClient: () => ({ readOutstanding }),
    localExecutionInspectionIndexClient: () => ({ readIndex }),
  }))
  const { default: FreshApp } = await import('./App')
  return { ...render(<FreshApp />), FreshApp, readOutstanding, readIndex }
}

const region = () => screen.getByRole('region', { name: 'Inspect outstanding queue admissions' })
const open = () => fireEvent.click(screen.getByRole('button', { name: 'Inspect outstanding queue admissions' }))

afterEach(() => {
  vi.useRealTimers()
  vi.doUnmock('./api/clients')
})

describe('the global outstanding queue admission region in the host session', () => {
  it('is shown collapsed beside the recorded-execution limit and reads nothing on mount, even with no project registered', async () => {
    const { readOutstanding } = await renderApp({ noProjects: true })

    expect(screen.getByRole('region', { name: 'Recorded-execution limit' })).toBeInTheDocument()
    expect(region()).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Inspect outstanding queue admissions' })).toHaveAttribute('aria-expanded', 'false')
    expect(readOutstanding).not.toHaveBeenCalled()
    const limit = screen.getByRole('region', { name: 'Recorded-execution limit' })
    expect(limit.compareDocumentPosition(region()) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy()
  })

  it('reads once when opened and keeps its answer, its selection and its single read across a project change', async () => {
    const { readOutstanding, readIndex } = await renderApp()
    open()
    expect(await screen.findByText(/Project 2/)).toBeInTheDocument()
    expect(readOutstanding).toHaveBeenCalledTimes(1)
    fireEvent.click(screen.getAllByRole('button', { name: /^Select recorded cycle/ })[1])
    expect(await screen.findByText(/Selected recorded cycle: project Project 2/)).toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: /^Beta/ }))

    expect(screen.getByText('cockpit run-b')).toBeInTheDocument()
    expect(screen.getByText(/Project 1/)).toBeInTheDocument()
    expect(screen.getByText(/Selected recorded cycle: project Project 2/)).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: /^Alpha/ }))
    expect(screen.getByText('cockpit run-a')).toBeInTheDocument()
    expect(screen.getByText(/Selected recorded cycle: project Project 2/)).toBeInTheDocument()
    expect(readOutstanding).toHaveBeenCalledTimes(1)
    expect(readIndex).not.toHaveBeenCalled()
  })

  it('selects a recorded cycle without changing the selected project or its run, and reads the inspector only on its own action', async () => {
    const { readIndex, readOutstanding } = await renderApp()
    open()
    await screen.findByText(/Project 2/)

    fireEvent.click(screen.getAllByRole('button', { name: /^Select recorded cycle/ })[0])
    const inspector = await screen.findByRole('region', { name: 'Inspect local execution' })

    expect(screen.getByText('cockpit run-a')).toBeInTheDocument()
    expect(screen.getByText(`workspace panel ${PROJECT_ID}`)).toBeInTheDocument()
    expect(readIndex).not.toHaveBeenCalled()
    fireEvent.click(inspector.querySelector('button') as HTMLButtonElement)
    await waitFor(() => expect(readIndex).toHaveBeenCalledTimes(1))
    expect(readIndex).toHaveBeenCalledWith(runIdFor(1))
    expect(readOutstanding).toHaveBeenCalledTimes(1)
  })

  it('is not refreshed by an evidence refresh, a re-render or the passing of time: there is no polling', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true })
    const { readOutstanding } = await renderApp()
    open()
    await screen.findByText(/Project 2/)
    expect(readOutstanding).toHaveBeenCalledTimes(1)

    fireEvent.click(screen.getByRole('button', { name: 'refresh evidence' }))
    await act(async () => vi.advanceTimersByTimeAsync(10 * 60_000))

    expect(readOutstanding).toHaveBeenCalledTimes(1)
    expect(screen.getByText(/Project 2/)).toBeInTheDocument()
  })

  it('reads again only on the explicit reload', async () => {
    const { readOutstanding } = await renderApp()
    open()
    await screen.findByText(/Project 2/)

    fireEvent.click(screen.getByRole('button', { name: 'Reload outstanding queue admissions' }))

    await waitFor(() => expect(readOutstanding).toHaveBeenCalledTimes(2))
  })

  it('does not exist before the session is ready and ends with the session: its rows and its selection do not survive a disconnect', async () => {
    let status = 'initializing'
    const { readOutstanding, rerender, FreshApp } = await renderApp({ sessionStatus: () => status })
    expect(screen.queryByRole('region', { name: 'Inspect outstanding queue admissions' })).toBeNull()
    expect(readOutstanding).not.toHaveBeenCalled()

    status = 'ready'
    rerender(<FreshApp />)
    open()
    await screen.findByText(/Project 2/)
    fireEvent.click(screen.getAllByRole('button', { name: /^Select recorded cycle/ })[0])
    await screen.findByText(/Selected recorded cycle/)

    status = 'disconnected'
    rerender(<FreshApp />)
    expect(screen.queryByRole('region', { name: 'Inspect outstanding queue admissions' })).toBeNull()
    expect(screen.queryByText(/Selected recorded cycle/)).toBeNull()

    status = 'ready'
    rerender(<FreshApp />)
    expect(screen.getByRole('button', { name: 'Inspect outstanding queue admissions' })).toHaveAttribute('aria-expanded', 'false')
    expect(screen.queryByText(/Project 2/)).toBeNull()
    expect(readOutstanding).toHaveBeenCalledTimes(1)
  })
})
