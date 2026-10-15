import { act, cleanup, fireEvent, render, screen, within } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { gitHubBranchPublicationApprovalClient, recordGitHubBranchPublicationApprovalClient } from '../../../api/clients'
import type {
  GetGitHubBranchPublicationApprovalResponse,
  RecordGitHubBranchPublicationApprovalRequest,
  RecordGitHubBranchPublicationApprovalResponse,
} from '../../../api/clients'
import type { ObservationScope, ObservationSource } from '../gitHubObservationTarget'
import { GitHubBranchPublicationApprovalPanel } from './GitHubBranchPublicationApprovalPanel'

vi.mock('../../../api/clients', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../../api/clients')>()),
  gitHubBranchPublicationApprovalClient: vi.fn(),
  recordGitHubBranchPublicationApprovalClient: vi.fn(),
}))

const SHA = 'c'.repeat(40)
const BRANCH = 'devalcopilot/workspace/abc/1'
const TARGET_AT = new Date('2026-10-14T12:00:00.000Z')
const OBSERVED = new Date('2026-10-14T12:01:00.000Z')
const BOUND = new Date('2026-10-14T12:02:00.000Z')
const APPROVED = new Date('2026-10-14T12:03:00.000Z')
const source: ObservationSource = { runId: 'run-a', operationId: 'op-a', commitSha: SHA, branchName: BRANCH }
const target: ObservationScope = {
  id: 'target-1',
  eventId: 'event-1',
  host: 'github.com',
  owner: 'octo-cat',
  repository: 'hello.world',
  recordedAtUtc: TARGET_AT,
}

const wireSource = { operationId: source.operationId, commitSha: source.commitSha, branchName: source.branchName }
const wireTarget = { ...target }
const wireBinding = {
  id: 'binding-1',
  eventId: 'binding-event-1',
  repositoryNodeId: 'R_kgDOExample01',
  owner: 'octo-cat',
  repository: 'hello.world',
  observedAtUtc: OBSERVED,
  recordedAtUtc: BOUND,
}
const wireApproval = (id: string) => ({
  id,
  eventId: 'approval-event-1',
  host: 'github.com',
  action: 'PushExactCommit',
  policyVersion: 1,
  localCommitOperationId: 'op-a',
  workspaceId: 'workspace-1',
  targetId: 'target-1',
  bindingId: 'binding-1',
  repositoryNodeId: 'R_kgDOExample01',
  owner: 'octo-cat',
  repository: 'hello.world',
  branchName: BRANCH,
  commitSha: SHA,
  approvedAtUtc: APPROVED,
})
const notApproved = () =>
  ({ runId: 'run-a', status: 'ApprovalNotRecorded', source: wireSource, target: wireTarget, binding: wireBinding, approval: null }) as unknown as GetGitHubBranchPublicationApprovalResponse
const alreadyApproved = () =>
  ({ runId: 'run-a', status: 'Available', source: wireSource, target: wireTarget, binding: wireBinding, approval: wireApproval('approval-1') }) as unknown as GetGitHubBranchPublicationApprovalResponse
const posted = (id: string) =>
  ({ runId: 'run-a', source: wireSource, target: wireTarget, binding: wireBinding, approval: wireApproval(id) }) as unknown as RecordGitHubBranchPublicationApprovalResponse

function install(
  overrides: Partial<{
    read: () => Promise<GetGitHubBranchPublicationApprovalResponse>
    record: (runId: string, request: RecordGitHubBranchPublicationApprovalRequest) => Promise<RecordGitHubBranchPublicationApprovalResponse>
  }> = {},
) {
  const read = vi.fn(overrides.read ?? (() => Promise.resolve(notApproved())))
  const record = vi.fn(overrides.record ?? ((_runId: string, request: RecordGitHubBranchPublicationApprovalRequest) => Promise.resolve(posted(request.approvalId))))
  vi.mocked(gitHubBranchPublicationApprovalClient).mockReturnValue(
    { getGitHubBranchPublicationApproval: read } as unknown as ReturnType<typeof gitHubBranchPublicationApprovalClient>,
  )
  vi.mocked(recordGitHubBranchPublicationApprovalClient).mockReturnValue(
    { recordGitHubBranchPublicationApproval: record } as unknown as ReturnType<typeof recordGitHubBranchPublicationApprovalClient>,
  )
  return { read, record }
}

async function click(element: HTMLElement) {
  await act(async () => {
    fireEvent.click(element)
  })
}

const region = () => screen.getByRole('region', { name: 'GitHub branch publication approval' })
const button = (name: string | RegExp) => within(region()).getByRole('button', { name })
const mountPanel = () => render(<GitHubBranchPublicationApprovalPanel projectId="project-1" source={source} target={target} />)

describe('GitHubBranchPublicationApprovalPanel', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  afterEach(() => {
    cleanup()
  })

  it('is collapsed with one Show action and sends nothing on mount, while waiting or on opening', async () => {
    vi.useFakeTimers()
    const mocks = install()
    mountPanel()
    act(() => {
      vi.advanceTimersByTime(10 * 60 * 1000)
    })
    vi.useRealTimers()

    expect(within(region()).getAllByRole('button')).toHaveLength(1)
    await click(button('Show GitHub branch publication approval'))

    expect(region().textContent).toContain('Not loaded.')
    expect(mocks.read).not.toHaveBeenCalled()
    expect(mocks.record).not.toHaveBeenCalled()
  })

  it('shows the exact destination, reference and commit with the fixed consent notice before anything is read, and enables nothing', async () => {
    install()
    mountPanel()

    await click(button('Show GitHub branch publication approval'))

    const text = region().textContent ?? ''
    expect(text).toContain('github.com / octo-cat / hello.world')
    expect(text).toContain(`refs/heads/${BRANCH}`)
    expect(text).toContain(SHA)
    expect(text).toMatch(/ONE future normal push/)
    expect(text).toMatch(/No push occurs now/)
    expect(text).toMatch(/pull request, force-push, merge, deletion, rerun and automatic execution are not authorized/)
    expect(button('Approve one future push')).toBeDisabled()
    expect(button('Load GitHub branch publication approval')).toBeEnabled()
  })

  it('loads on the explicit action alone, shows the bound repository ID and enables the approval only for the settled not-approved read', async () => {
    const mocks = install()
    mountPanel()
    await click(button('Show GitHub branch publication approval'))

    await click(button('Load GitHub branch publication approval'))

    expect(mocks.read).toHaveBeenCalledTimes(1)
    expect(mocks.read).toHaveBeenCalledWith('run-a')
    expect(region().textContent).toContain('R_kgDOExample01')
    expect(region().textContent).toContain('No GitHub branch publication approval is recorded for this local delivery yet.')
    expect(button('Approve one future push')).toBeEnabled()
    expect(button('Reload GitHub branch publication approval')).toBeEnabled()
    expect(mocks.record).not.toHaveBeenCalled()
  })

  it('records one approval on the explicit action, shows the recorded consent and offers no execution or second approval', async () => {
    const mocks = install()
    mountPanel()
    await click(button('Show GitHub branch publication approval'))
    await click(button('Load GitHub branch publication approval'))

    await click(button('Approve one future push'))

    expect(mocks.record).toHaveBeenCalledTimes(1)
    const [runId, request] = mocks.record.mock.calls[0] as [string, RecordGitHubBranchPublicationApprovalRequest]
    expect(runId).toBe('run-a')
    expect(request).toMatchObject({
      operationId: 'op-a',
      commitSha: SHA,
      branchName: BRANCH,
      targetId: 'target-1',
      bindingId: 'binding-1',
      repositoryNodeId: 'R_kgDOExample01',
    })
    expect(request.approvalId).toMatch(/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/)
    const text = region().textContent ?? ''
    expect(text).toMatch(/Recorded consent/)
    expect(text).toContain(APPROVED.toISOString())
    expect(text).toContain('approval-event-1')
    expect(within(region()).queryByRole('button', { name: 'Approve one future push' })).toBeNull()
    const labels = within(region()).getAllByRole('button').map((element) => element.textContent)
    expect(labels).toEqual(['Reload GitHub branch publication approval', 'Close GitHub branch publication approval'])
    for (const label of labels) {
      expect(label).not.toMatch(/push now|execute|run |publish|create (a )?pull|merge/i)
    }
  })

  it('shows an already recorded approval as recorded consent only, with the same labels and no approval action', async () => {
    install({ read: () => Promise.resolve(alreadyApproved()) })
    mountPanel()
    await click(button('Show GitHub branch publication approval'))
    await click(button('Load GitHub branch publication approval'))

    const text = region().textContent ?? ''
    expect(text).toMatch(/Recorded consent/)
    expect(text).toMatch(/not a push/)
    expect(text).toContain('approval-1')
    expect(within(region()).queryByRole('button', { name: 'Approve one future push' })).toBeNull()
  })

  it.each([
    ['Unavailable', { runId: 'run-a', status: 'Unavailable', source: null, target: null, binding: null, approval: null }, /Unavailable: the recorded facts/],
    ['NotRecorded', { runId: 'run-a', status: 'NotRecorded', source: null, target: null, binding: null, approval: null }, /has no recorded target and repository binding/],
  ])('shows %s as its fixed statement with no approval offered', async (_name, response, statement) => {
    install({ read: () => Promise.resolve(response as unknown as GetGitHubBranchPublicationApprovalResponse) })
    mountPanel()
    await click(button('Show GitHub branch publication approval'))
    await click(button('Load GitHub branch publication approval'))

    expect(region().textContent).toMatch(statement)
    expect(button('Approve one future push')).toBeDisabled()
  })

  it('shows a fixed alert for a failed or incoherent read and never echoes host text', async () => {
    install({ read: () => Promise.reject(new Error('sentinel host text')) })
    mountPanel()
    await click(button('Show GitHub branch publication approval'))
    await click(button('Load GitHub branch publication approval'))

    expect(within(region()).getByRole('alert').textContent).toBe('The recorded GitHub branch publication approval could not be read.')
    expect(region().textContent).not.toContain('sentinel')
    expect(button('Approve one future push')).toBeDisabled()

    cleanup()
    install({ read: () => Promise.resolve({ ...notApproved(), runId: 'run-other' } as GetGitHubBranchPublicationApprovalResponse) })
    mountPanel()
    await click(button('Show GitHub branch publication approval'))
    await click(button('Load GitHub branch publication approval'))

    expect(within(region()).getByRole('alert').textContent).toMatch(/not coherent for this delivery/)
  })

  it('clears the reading to Not loaded and keeps a fixed alert after a failed approval, until an explicit reload', async () => {
    const mocks = install({ record: () => Promise.reject(new Error('sentinel host text')) })
    mountPanel()
    await click(button('Show GitHub branch publication approval'))
    await click(button('Load GitHub branch publication approval'))
    expect(region().textContent).toContain('R_kgDOExample01')

    await click(button('Approve one future push'))

    expect(within(region()).getByRole('alert').textContent).toMatch(/outcome of approving is unknown/)
    expect(region().textContent).toContain('Not loaded.')
    expect(region().textContent).not.toContain('R_kgDOExample01')
    expect(region().textContent).not.toContain('sentinel')
    expect(button('Approve one future push')).toBeDisabled()
    expect(mocks.record).toHaveBeenCalledTimes(1)

    await click(button('Load GitHub branch publication approval'))

    expect(within(region()).queryByRole('alert')).toBeNull()
    expect(button('Approve one future push')).toBeEnabled()
    expect(mocks.read).toHaveBeenCalledTimes(2)
  })

  it('states a definite refusal in its own fixed words', async () => {
    const { ApiException } = await import('../../../api/generated/api-client')
    install({ record: () => Promise.reject(new ApiException('conflict', 409, '', {}, null)) })
    mountPanel()
    await click(button('Show GitHub branch publication approval'))
    await click(button('Load GitHub branch publication approval'))

    await click(button('Approve one future push'))

    expect(within(region()).getByRole('alert').textContent).toMatch(/The host refused this approval/)
    expect(button('Approve one future push')).toBeDisabled()
  })

  it('closes back to the collapsed action and starts blank when shown again', async () => {
    const mocks = install()
    mountPanel()
    await click(button('Show GitHub branch publication approval'))
    await click(button('Load GitHub branch publication approval'))

    await click(button('Close GitHub branch publication approval'))
    expect(within(region()).getAllByRole('button')).toHaveLength(1)
    await click(button('Show GitHub branch publication approval'))

    expect(region().textContent).toContain('Not loaded.')
    expect(button('Approve one future push')).toBeDisabled()
    expect(mocks.read).toHaveBeenCalledTimes(1)
  })

  it('renders hostile text as inert text with no link, input or extra action', async () => {
    const hostile = '<img src=x onerror=alert(1)>'
    install({
      read: () =>
        Promise.resolve({ ...notApproved(), binding: { ...wireBinding, repositoryNodeId: 'R_ok' } } as unknown as GetGitHubBranchPublicationApprovalResponse),
    })
    render(
      <GitHubBranchPublicationApprovalPanel
        projectId="project-1"
        source={{ ...source, branchName: hostile }}
        target={target}
      />,
    )
    await click(button('Show GitHub branch publication approval'))

    expect(region().querySelector('img')).toBeNull()
    expect(region().textContent).toContain(hostile)
    expect(region().querySelector('a')).toBeNull()
    expect(region().querySelector('input')).toBeNull()
    expect(region().querySelector('textarea')).toBeNull()
  })
})
