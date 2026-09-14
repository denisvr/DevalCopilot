import type { ProjectRunSummaryResponse } from '../../../api/clients'

interface ProjectBaselineSummaryProps {
  project: ProjectRunSummaryResponse
}

function abbreviate(sha: string | undefined): string {
  return sha ? sha.slice(0, 7) : ''
}

/** Renders exactly the safe baseline metadata this slice captures — never a fabricated state
 * for a project with no baseline at all (a legacy or otherwise never-validated row). */
function formatHead(project: ProjectRunSummaryResponse): string {
  if (!project.headState) {
    return 'not yet validated'
  }

  if (project.headState === 'Detached') {
    return `detached @ ${abbreviate(project.headCommitSha)}`
  }

  if (project.headState === 'Unborn') {
    return `${project.branchName} (no commits yet)`
  }

  return `${project.branchName} @ ${abbreviate(project.headCommitSha)}`
}

export function ProjectBaselineSummary({ project }: ProjectBaselineSummaryProps) {
  return (
    <div className="dc-project-baseline" aria-label="Repository baseline">
      <span className="dc-project-baseline-path">{project.canonicalPath}</span>
      <span className="dc-project-baseline-head">{formatHead(project)}</span>
      {project.headState ? (
        <span className="dc-project-baseline-dirty" data-dirty={project.isDirty}>
          {project.isDirty ? 'dirty' : 'clean'}
        </span>
      ) : null}
    </div>
  )
}
