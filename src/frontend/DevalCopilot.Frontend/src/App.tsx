import { useState } from 'react'
import { startSimulatedRunClient } from './api/clients'
import { StartSimulatedRunRequest } from './api/generated/api-client'
import { ProjectSwitcher } from './features/cockpit/components/ProjectSwitcher'
import { RunCockpitView } from './features/cockpit/components/RunCockpitView'
import { useProjectSummaries } from './features/cockpit/hooks/useProjectSummaries'
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
  const [selectedProjectId, setSelectedProjectId] = useState<string | null>(null)
  const [starting, setStarting] = useState(false)

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

  async function handleStartRun(projectId: string) {
    setStarting(true)
    try {
      await startSimulatedRunClient().startSimulatedRun(
        new StartSimulatedRunRequest({ projectId, objective: 'Prove the walking skeleton' }),
      )
      refresh()
    } finally {
      setStarting(false)
    }
  }

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
        <button
          type="button"
          className="dc-theme-toggle"
          onClick={() => setTheme(theme === 'dark-navy' ? 'light' : 'dark-navy')}
        >
          {theme === 'dark-navy' ? '☾ Dark Navy' : '☀ Light'}
        </button>
      </header>

      {selectedProject?.runId ? (
        <RunCockpitView runId={selectedProject.runId} />
      ) : selectedProject ? (
        <div className="dc-empty-state">
          <p>{selectedProject.projectName} has no run yet.</p>
          <button
            type="button"
            className="dc-button"
            data-variant="primary"
            disabled={starting}
            onClick={() => handleStartRun(selectedProject.projectId ?? '')}
          >
            {starting ? 'Starting…' : 'Start simulated run'}
          </button>
        </div>
      ) : (
        !loading && <p className="dc-empty-state">No projects are registered yet.</p>
      )}
    </div>
  )
}
