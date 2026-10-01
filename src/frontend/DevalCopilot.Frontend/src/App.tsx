import { useCallback, useState } from 'react'
import { AddProjectForm } from './features/cockpit/components/AddProjectForm'
import { CandidateWorkspacePanel } from './features/cockpit/components/CandidateWorkspacePanel'
import { CapabilityReadinessStrip } from './features/cockpit/components/CapabilityReadinessStrip'
import { ProviderRuntimePreflight } from './features/cockpit/components/ProviderRuntimePreflight'
import { ProjectBaselineSummary } from './features/cockpit/components/ProjectBaselineSummary'
import { ProjectSwitcher } from './features/cockpit/components/ProjectSwitcher'
import { RunIntakeForm } from './features/cockpit/components/RunIntakeForm'
import { RunCockpitView } from './features/cockpit/components/RunCockpitView'
import { useHostCapabilityRefresh } from './features/cockpit/hooks/useHostCapabilityRefresh'
import { useProjectSummaries } from './features/cockpit/hooks/useProjectSummaries'
import { useProviderRuntimePreflight } from './features/cockpit/hooks/useProviderRuntimePreflight'
import { useSessionStatus } from './features/cockpit/hooks/useSessionStatus'
import { useTheme } from './features/cockpit/hooks/useTheme'
import './features/cockpit/cockpit.css'

function StartingState() {
  return (
    <div className="dc-app">
      <p className="dc-empty-state">Starting the local host…</p>
    </div>
  )
}

function SidecarFailedState() {
  return (
    <div className="dc-app">
      <p className="dc-empty-state">
        No launch session is available. Open this application through the packaged desktop shell, or through the
        Playwright test harness, to authenticate.
      </p>
    </div>
  )
}

function DisconnectedRecoveryState() {
  return (
    <div className="dc-app">
      <p className="dc-empty-state">
        The local host disconnected. Close and reopen the application to start a new session — a previous session is
        never reused.
      </p>
    </div>
  )
}

export default function App() {
  const [theme, setTheme] = useTheme()
  const sessionStatus = useSessionStatus()
  const { projects, loading, error, refresh } = useProjectSummaries(sessionStatus === 'ready')
  const {
    providers: providerRuntimes,
    loading: providerRuntimeLoading,
    error: providerRuntimeError,
    refresh: refreshProviderRuntimes,
  } = useProviderRuntimePreflight(sessionStatus === 'ready')
  const refreshReadiness = useCallback(() => {
    refresh()
    refreshProviderRuntimes()
  }, [refresh, refreshProviderRuntimes])
  const { refreshingCapability, requestRefresh } = useHostCapabilityRefresh(refreshReadiness)
  const [selectedProjectId, setSelectedProjectId] = useState<string | null>(null)

  if (sessionStatus === 'idle' || sessionStatus === 'initializing') {
    return <StartingState />
  }

  if (sessionStatus === 'failed') {
    return <SidecarFailedState />
  }

  if (sessionStatus === 'disconnected') {
    return <DisconnectedRecoveryState />
  }

  const selectedProject = projects.find((project) => project.projectId === selectedProjectId) ?? projects[0] ?? null

  // The server's availability hint: true for a project with no run, or when every run is terminal.
  // A project with a run and no explicit hint is treated as blocked; the server re-checks anyway.
  const canCreateRun = selectedProject?.runId ? selectedProject.canCreateRun === true : selectedProject?.canCreateRun !== false && selectedProject !== null

  return (
    <div className="dc-app">
      <header className="dc-topbar">
        <button type="button" className="dc-nav-toggle" aria-label="Global navigation" disabled title="Not available in this increment">
          ☰
        </button>
        {loading ? (
          <span className="dc-project-switcher">Loading projects…</span>
        ) : error ? (
          <span className="dc-project-switcher">{error}</span>
        ) : (
          <ProjectSwitcher
            projects={projects}
            selectedProjectId={selectedProject?.projectId ?? null}
            onSelect={setSelectedProjectId}
          />
        )}
        <AddProjectForm onRegistered={refresh} />
        <button
          type="button"
          className="dc-theme-toggle"
          onClick={() => setTheme(theme === 'dark-navy' ? 'light' : 'dark-navy')}
        >
          {theme === 'dark-navy' ? '☾ Dark Navy' : '☀ Light'}
        </button>
      </header>

      {selectedProject ? <ProjectBaselineSummary project={selectedProject} /> : null}

      {selectedProject?.projectId ? <CandidateWorkspacePanel projectId={selectedProject.projectId} /> : null}

      {selectedProject ? (
        <CapabilityReadinessStrip
          capabilities={selectedProject.capabilities ?? []}
          refreshingCapability={refreshingCapability}
          onRefresh={requestRefresh}
        />
      ) : null}

      <ProviderRuntimePreflight
        providers={providerRuntimes}
        loading={providerRuntimeLoading}
        error={providerRuntimeError}
        refreshingCapability={refreshingCapability}
        onRefresh={requestRefresh}
      />

      {selectedProject ? (
        <RunIntakeForm
          projectId={selectedProject.projectId ?? ''}
          canCreateRun={canCreateRun}
          onChanged={refresh}
        />
      ) : null}

      {selectedProject?.runId ? (
        <RunCockpitView runId={selectedProject.runId} />
      ) : selectedProject ? (
        <p className="dc-empty-state">{selectedProject.projectName} has no run yet.</p>
      ) : (
        !loading && <p className="dc-empty-state">No projects are registered yet.</p>
      )}
    </div>
  )
}
