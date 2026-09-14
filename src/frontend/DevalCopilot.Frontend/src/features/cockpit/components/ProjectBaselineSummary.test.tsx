import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { ProjectRunSummaryResponse } from '../../../api/generated/api-client'
import { ProjectBaselineSummary } from './ProjectBaselineSummary'

function project(overrides: Partial<ProjectRunSummaryResponse>): ProjectRunSummaryResponse {
  return new ProjectRunSummaryResponse({
    projectId: 'project-1',
    projectName: 'DevalCopilot',
    canonicalPath: String.raw`C:\repos\DevalCopilot`,
    capabilities: [],
    isDirty: false,
    ...overrides,
  })
}

describe('ProjectBaselineSummary', () => {
  it('renders a project with no baseline as not yet validated, never fabricating clean/branch data', () => {
    render(<ProjectBaselineSummary project={project({ headState: undefined })} />)

    expect(screen.getByText('not yet validated')).toBeInTheDocument()
    expect(screen.queryByText(/clean|dirty/)).not.toBeInTheDocument()
  })

  it('renders a clean OnBranch baseline with the abbreviated SHA', () => {
    render(
      <ProjectBaselineSummary
        project={project({
          headState: 'OnBranch',
          branchName: 'main',
          headCommitSha: '767d4ccafa03b7ed295d11862de3e0c0df693377',
          isDirty: false,
        })}
      />,
    )

    expect(screen.getByText('main @ 767d4cc')).toBeInTheDocument()
    expect(screen.getByText('clean')).toBeInTheDocument()
  })

  it('renders a dirty OnBranch baseline', () => {
    render(
      <ProjectBaselineSummary
        project={project({ headState: 'OnBranch', branchName: 'main', headCommitSha: 'a'.repeat(40), isDirty: true })}
      />,
    )

    expect(screen.getByText('dirty')).toBeInTheDocument()
  })

  it('renders a detached baseline without a branch name', () => {
    render(
      <ProjectBaselineSummary
        project={project({ headState: 'Detached', branchName: undefined, headCommitSha: 'b'.repeat(40) })}
      />,
    )

    expect(screen.getByText(`detached @ ${'b'.repeat(7)}`)).toBeInTheDocument()
  })

  it('renders an unborn baseline with no commit yet', () => {
    render(
      <ProjectBaselineSummary
        project={project({ headState: 'Unborn', branchName: 'main', headCommitSha: undefined })}
      />,
    )

    expect(screen.getByText('main (no commits yet)')).toBeInTheDocument()
  })

  it('always renders the canonical path as plain text', () => {
    render(<ProjectBaselineSummary project={project({ headState: undefined })} />)

    expect(screen.getByText(String.raw`C:\repos\DevalCopilot`)).toBeInTheDocument()
  })
})
