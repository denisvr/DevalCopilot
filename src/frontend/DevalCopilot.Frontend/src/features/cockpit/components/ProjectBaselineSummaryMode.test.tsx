import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { ProjectRunSummaryResponse } from '../../../api/generated/api-client'
import { ProjectBaselineSummary } from './ProjectBaselineSummary'

function project(overrides: Partial<ProjectRunSummaryResponse>) {
  return new ProjectRunSummaryResponse({
    projectId: 'project-1',
    projectName: 'DevalCopilot',
    canonicalPath: 'C:/repo',
    capabilities: [],
    isDirty: false,
    ...overrides,
  })
}

describe('ProjectBaselineSummary execution mode', () => {
  it('shows nothing about a mode for a project without a run', () => {
    render(<ProjectBaselineSummary project={project({})} />)
    expect(screen.queryByText(/run|mode/i)).not.toBeInTheDocument()
  })

  it.each([
    ['ManualAgent', 'Manual Agent run'],
    ['Simulated', 'Simulated demo run'],
    ['Legacy', 'Legacy run — execution mode was not recorded'],
    ['Unrecognized', 'Unrecognized execution mode'],
    [undefined, 'Unrecognized execution mode'],
  ])('summarizes a %s run truthfully', (executionMode, label) => {
    render(<ProjectBaselineSummary project={project({ runId: 'run-1', executionMode })} />)
    expect(screen.getByText(label)).toBeInTheDocument()
  })
})
