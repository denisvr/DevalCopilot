import type { ProjectRunSummaryResponse } from '../../../api/clients'

interface ProjectSwitcherProps {
  projects: ProjectRunSummaryResponse[]
  selectedProjectId: string | null
  onSelect: (projectId: string) => void
}

/**
 * Shows every registered project with its most relevant run. Selecting a project
 * replaces the entire cockpit projection atomically; it never combines state from
 * two runs.
 */
export function ProjectSwitcher({ projects, selectedProjectId, onSelect }: ProjectSwitcherProps) {
  return (
    <nav className="dc-project-switcher" aria-label="Projects">
      {projects.map((project) => (
        <button
          key={project.projectId}
          type="button"
          className="dc-project-chip"
          data-selected={project.projectId === selectedProjectId}
          onClick={() => onSelect(project.projectId ?? '')}
        >
          {project.projectName}
          {project.lifecycle ? ` · ${project.lifecycle}` : ' · No runs yet'}
        </button>
      ))}
    </nav>
  )
}
