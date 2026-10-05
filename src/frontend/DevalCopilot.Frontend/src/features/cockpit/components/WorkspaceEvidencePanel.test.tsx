import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import {
  captureGitWorkspaceCheckpointClient,
  gitCheckpointChangedFilesClient,
  gitCheckpointDiffClient,
  projectGitEvidenceClient,
} from '../../../api/clients'
import {
  GetGitCheckpointDiffOmissionResponse,
  GetGitCheckpointDiffResponse,
  GetProjectGitEvidenceResponse,
  GitCheckpointChangedFileResponse,
} from '../../../api/generated/api-client'
import { WorkspaceEvidencePanel } from './WorkspaceEvidencePanel'

vi.mock('../../../api/clients', () => ({
  projectGitEvidenceClient: vi.fn(),
  captureGitWorkspaceCheckpointClient: vi.fn(),
  gitCheckpointChangedFilesClient: vi.fn(),
  gitCheckpointDiffClient: vi.fn(),
}))

const LIMITATION = 'Host comparison of attested tracked sources, not Git\'s minimal or filter-normalized patch.'

function installClients(diff: GetGitCheckpointDiffResponse | Promise<GetGitCheckpointDiffResponse>) {
  vi.mocked(projectGitEvidenceClient).mockReturnValue({
    getProjectGitEvidence: vi.fn().mockResolvedValue(new GetProjectGitEvidenceResponse({
      checkpointId: 'checkpoint-1',
      checkpointNumber: 1,
      headCommitSha: 'a'.repeat(40),
      fingerprintSha256: 'b'.repeat(64),
      changedFileCount: 1,
    })),
  } as unknown as ReturnType<typeof projectGitEvidenceClient>)
  vi.mocked(captureGitWorkspaceCheckpointClient).mockReturnValue({
    captureGitWorkspaceCheckpoint: vi.fn(),
  } as unknown as ReturnType<typeof captureGitWorkspaceCheckpointClient>)
  vi.mocked(gitCheckpointChangedFilesClient).mockReturnValue({
    getGitCheckpointChangedFiles: vi.fn().mockResolvedValue([
      new GitCheckpointChangedFileResponse({ path: 'src/App.tsx', indexStatus: ' ', workTreeStatus: 'M' }),
    ]),
  } as unknown as ReturnType<typeof gitCheckpointChangedFilesClient>)
  const getGitCheckpointDiff = vi.fn().mockReturnValue(Promise.resolve(diff))
  vi.mocked(gitCheckpointDiffClient).mockReturnValue({ getGitCheckpointDiff } as unknown as ReturnType<typeof gitCheckpointDiffClient>)
  return { getGitCheckpointDiff }
}

async function inspect() {
  render(<WorkspaceEvidencePanel projectId="project-1" workspaceReady />)
  expect(await screen.findByText('Checkpoint #1 · 1 changed files')).toBeInTheDocument()
  fireEvent.click(screen.getByText('Inspect files & diff'))
  return screen.findByRole('group', { name: 'Checkpoint comparison' })
}

describe('WorkspaceEvidencePanel', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('shows only checkpoint-bound changed files and the host comparison after a fresh inspection request', async () => {
    installClients(new GetGitCheckpointDiffResponse({
      fingerprintSha256: 'b'.repeat(64),
      comparisonText: 'diff --git a/src/App.tsx b/src/App.tsx',
      isComplete: true,
      trackedPathCount: 1,
      comparedPathCount: 1,
      limitation: LIMITATION,
      omissions: [],
    }))

    render(<WorkspaceEvidencePanel projectId="project-1" workspaceReady />)

    expect(await screen.findByText('Checkpoint #1 · 1 changed files')).toBeInTheDocument()
    expect(screen.queryByText('src/App.tsx')).not.toBeInTheDocument()
    expect(screen.queryByRole('group', { name: 'Checkpoint comparison' })).not.toBeInTheDocument()

    fireEvent.click(screen.getByText('Inspect files & diff'))

    await waitFor(() => expect(screen.getByText('src/App.tsx')).toBeInTheDocument())
    expect(screen.getByText('diff --git a/src/App.tsx b/src/App.tsx')).toBeInTheDocument()
    expect(screen.getByRole('status')).toHaveTextContent('All 1 tracked file compared.')
    expect(screen.getByText(LIMITATION)).toBeInTheDocument()
    expect(screen.queryByRole('list', { name: 'Files without comparison' })).not.toBeInTheDocument()
  })

  it('states a partial comparison, lists each omitted path with its reason and never calls the capture clean', async () => {
    installClients(new GetGitCheckpointDiffResponse({
      comparisonText: 'diff --git a/src/safe.txt b/src/safe.txt',
      isComplete: false,
      trackedPathCount: 2,
      comparedPathCount: 1,
      limitation: LIMITATION,
      omissions: [new GetGitCheckpointDiffOmissionResponse({ path: 'src/linked.txt', reason: 'containment_unproven' })],
    }))

    const comparison = await inspect()

    expect(within(comparison).getByRole('status')).toHaveTextContent(
      'Partial comparison: 1 of 2 tracked files compared, 1 omitted.',
    )
    expect(within(comparison).getByText('diff --git a/src/safe.txt b/src/safe.txt')).toBeInTheDocument()
    const omissions = within(comparison).getByRole('list', { name: 'Files without comparison' })
    expect(within(omissions).getByText('src/linked.txt')).toBeInTheDocument()
    expect(omissions).toHaveTextContent('not proven to be this file inside the owned workspace')
    expect(screen.queryByText('No tracked diff.')).not.toBeInTheDocument()
  })

  it('states an all-omitted capture explicitly and never as an empty or clean diff', async () => {
    installClients(new GetGitCheckpointDiffResponse({
      comparisonText: '',
      isComplete: false,
      trackedPathCount: 2,
      comparedPathCount: 0,
      limitation: LIMITATION,
      omissions: [
        new GetGitCheckpointDiffOmissionResponse({ path: 'a.txt', reason: 'containment_unproven' }),
        new GetGitCheckpointDiffOmissionResponse({ path: 'b.bin', reason: 'binary' }),
      ],
    }))

    const comparison = await inspect()

    expect(within(comparison).getByRole('status')).toHaveTextContent('No tracked file could be compared: 2 omitted.')
    expect(within(comparison).getByText('a.txt')).toBeInTheDocument()
    expect(within(comparison).getByText('b.bin')).toBeInTheDocument()
    expect(screen.queryByText('No tracked diff.')).not.toBeInTheDocument()
    expect(comparison.querySelector('pre')).toBeNull()
  })

  it('shows the empty state only for a complete capture with no tracked change', async () => {
    installClients(new GetGitCheckpointDiffResponse({
      comparisonText: '',
      isComplete: true,
      trackedPathCount: 0,
      comparedPathCount: 0,
      limitation: LIMITATION,
      omissions: [],
    }))

    const comparison = await inspect()

    expect(within(comparison).getByRole('status')).toHaveTextContent('No tracked diff.')
    expect(comparison.querySelector('pre')).toBeNull()
  })

  it('renders omitted names, reasons and comparison text only as plain text', async () => {
    const hostile = '<img src=x onerror="document.title=1">'
    installClients(new GetGitCheckpointDiffResponse({
      comparisonText: `+${hostile}\n<script>document.title = 2</script>`,
      isComplete: false,
      trackedPathCount: 2,
      comparedPathCount: 1,
      limitation: `${LIMITATION} <b>bold</b>`,
      omissions: [new GetGitCheckpointDiffOmissionResponse({ path: `${hostile}.txt`, reason: '<i>unknown</i>' })],
    }))

    const comparison = await inspect()

    expect(comparison.querySelector('img, script, b, i')).toBeNull()
    expect(within(comparison).getByText(`${hostile}.txt`)).toBeInTheDocument()
    expect(comparison).toHaveTextContent('<i>unknown</i>')
    expect(comparison).toHaveTextContent('<script>document.title = 2</script>')
  })

  it('clears the previous comparison when a later inspection fails', async () => {
    const { getGitCheckpointDiff } = installClients(new GetGitCheckpointDiffResponse({
      comparisonText: 'diff --git a/first b/first',
      isComplete: true,
      trackedPathCount: 1,
      comparedPathCount: 1,
      omissions: [],
    }))

    await inspect()
    expect(screen.getByText('diff --git a/first b/first')).toBeInTheDocument()
    getGitCheckpointDiff.mockReturnValueOnce(Promise.reject(new Error('refused')))
    fireEvent.click(screen.getByText('Inspect files & diff'))

    await waitFor(() => expect(screen.getByText('This source evidence request could not be completed.')).toBeInTheDocument())
    expect(screen.queryByText('diff --git a/first b/first')).not.toBeInTheDocument()
    expect(screen.queryByRole('group', { name: 'Checkpoint comparison' })).not.toBeInTheDocument()
  })
})

// R1: coverage is admitted only from explicit, finite, non-negative integer counts with coherent accounting. Anything else is a
// fixed refusal through the failed-inspection path: no clean or complete summary and no comparison is shown.
describe('WorkspaceEvidencePanel contract admission', () => {
  const UNCONFIRMED = 'The host response could not be confirmed as a valid checkpoint comparison.'
  const omission = (path: string) => new GetGitCheckpointDiffOmissionResponse({ path, reason: 'binary' })
  const valid = {
    comparisonText: 'diff --git a/a.txt b/a.txt',
    isComplete: true,
    trackedPathCount: 1,
    comparedPathCount: 1,
    omissions: [],
  }
  const response = (overrides: Record<string, unknown>) =>
    new GetGitCheckpointDiffResponse({ ...valid, ...overrides } as unknown as ConstructorParameters<typeof GetGitCheckpointDiffResponse>[0])
  const without = (member: string) => {
    const copy: Record<string, unknown> = { ...valid }
    delete copy[member]
    return new GetGitCheckpointDiffResponse(copy as ConstructorParameters<typeof GetGitCheckpointDiffResponse>[0])
  }

  const refused: [string, () => GetGitCheckpointDiffResponse][] = [
    ['reviewer reproduction 1: only isComplete', () => new GetGitCheckpointDiffResponse({ isComplete: true })],
    [
      'reviewer reproduction 2: three tracked, none compared, none omitted',
      () => response({ trackedPathCount: 3, comparedPathCount: 0, comparisonText: '', omissions: [] }),
    ],
    ['missing tracked count', () => without('trackedPathCount')],
    ['missing compared count', () => without('comparedPathCount')],
    ['missing omissions', () => without('omissions')],
    ['missing comparison text', () => without('comparisonText')],
    ['missing completeness flag', () => without('isComplete')],
    ['NaN count', () => response({ trackedPathCount: Number.NaN })],
    ['infinite count', () => response({ comparedPathCount: Number.POSITIVE_INFINITY })],
    ['negative count', () => response({ trackedPathCount: -1, comparedPathCount: -1 })],
    ['fractional count', () => response({ trackedPathCount: 1.5 })],
    ['string count', () => response({ trackedPathCount: '1' })],
    ['unsafe integer count', () => response({ trackedPathCount: 2 ** 60, comparedPathCount: 2 ** 60 })],
    ['compared and omitted do not add up to tracked', () => response({ trackedPathCount: 3, comparedPathCount: 1, isComplete: false, omissions: [omission('x')] })],
    ['complete flag with an omission', () => response({ trackedPathCount: 2, omissions: [omission('x')] })],
    ['incomplete flag without an omission', () => response({ isComplete: false })],
    ['text with no compared path', () => response({ trackedPathCount: 1, comparedPathCount: 0, isComplete: false, omissions: [omission('x')] })],
    ['compared paths without text', () => response({ comparisonText: '' })],
    ['omission without a path', () => response({ trackedPathCount: 2, isComplete: false, omissions: [omission('')] })],
    ['duplicate omitted paths', () => response({ trackedPathCount: 3, isComplete: false, omissions: [omission('x'), omission('x')] })],
  ]

  beforeEach(() => {
    vi.clearAllMocks()
  })

  it.each(refused)('refuses with a fixed message and shows no coverage or comparison: %s', async (_name, build) => {
    installClients(build())

    render(<WorkspaceEvidencePanel projectId="project-1" workspaceReady />)
    expect(await screen.findByText('Checkpoint #1 · 1 changed files')).toBeInTheDocument()
    fireEvent.click(screen.getByText('Inspect files & diff'))

    expect(await screen.findByText(UNCONFIRMED)).toBeInTheDocument()
    expect(screen.queryByText('No tracked diff.')).not.toBeInTheDocument()
    expect(screen.queryByText(/tracked files? compared/)).not.toBeInTheDocument()
    expect(screen.queryByRole('group', { name: 'Checkpoint comparison' })).not.toBeInTheDocument()
    expect(screen.queryByRole('status')).not.toBeInTheDocument()
  })

  it('clears a previously shown comparison when a later answer is refused', async () => {
    const { getGitCheckpointDiff } = installClients(response({}))
    await inspect()
    expect(screen.getByRole('status')).toHaveTextContent('All 1 tracked file compared.')
    getGitCheckpointDiff.mockReturnValueOnce(Promise.resolve(new GetGitCheckpointDiffResponse({ isComplete: true })))

    fireEvent.click(screen.getByText('Inspect files & diff'))

    expect(await screen.findByText(UNCONFIRMED)).toBeInTheDocument()
    expect(screen.queryByRole('group', { name: 'Checkpoint comparison' })).not.toBeInTheDocument()
  })

  it.each([
    ['a genuinely empty capture', { comparisonText: '', trackedPathCount: 0, comparedPathCount: 0 }, 'No tracked diff.'],
    ['a complete capture', { trackedPathCount: 2, comparedPathCount: 2, comparisonText: 'diff --git a/a b/a\ndiff --git a/b b/b' }, 'All 2 tracked files compared.'],
    [
      'a partial capture',
      { isComplete: false, trackedPathCount: 3, comparedPathCount: 1, omissions: [omission('x'), omission('y')] },
      'Partial comparison: 1 of 3 tracked files compared, 2 omitted.',
    ],
    [
      'an all-omitted capture',
      { isComplete: false, comparisonText: '', trackedPathCount: 2, comparedPathCount: 0, omissions: [omission('x'), omission('y')] },
      'No tracked file could be compared: 2 omitted.',
    ],
  ])('still admits %s', async (_name, overrides, summary) => {
    installClients(response(overrides))

    const comparison = await inspect()

    expect(within(comparison).getByRole('status')).toHaveTextContent(summary)
    expect(screen.queryByText(UNCONFIRMED)).not.toBeInTheDocument()
  })
})
