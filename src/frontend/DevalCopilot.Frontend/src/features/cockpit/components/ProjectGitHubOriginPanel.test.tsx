import { act, cleanup, fireEvent, render, screen } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { projectGitHubOriginClient } from '../../../api/clients'
import type { ProjectGitHubOriginResponse } from '../../../api/clients'
import { ProjectGitHubOriginPanel } from './ProjectGitHubOriginPanel'

vi.mock('../../../api/clients', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../../api/clients')>()),
  projectGitHubOriginClient: vi.fn(),
}))

const A = 'aaaaaaaa-0000-0000-0000-00000000000a'
const B = 'bbbbbbbb-0000-0000-0000-00000000000b'
const AT = new Date('2026-10-12T09:00:00.000Z')

const wire = (projectId: string, status: string, target?: { owner: string; repository: string }) =>
  ({
    projectId,
    source: 'RepositoryLocalConfig',
    status,
    observedAtUtc: status === 'Unavailable' ? undefined : AT,
    target: target ? { host: 'github.com', ...target } : undefined,
  }) as unknown as ProjectGitHubOriginResponse

function install(read: (projectId: string) => Promise<ProjectGitHubOriginResponse>) {
  const getProjectGitHubOrigin = vi.fn(read)
  vi.mocked(projectGitHubOriginClient).mockReturnValue(
    { getProjectGitHubOrigin } as unknown as ReturnType<typeof projectGitHubOriginClient>,
  )
  return getProjectGitHubOrigin
}

async function click(element: HTMLElement) {
  await act(async () => {
    fireEvent.click(element)
  })
}

const show = () => screen.getByRole('button', { name: 'Show project GitHub origin' })
const inspect = () => screen.getByRole('button', { name: /^Inspect (GitHub origin|again)$/ })
const close = () => screen.getByRole('button', { name: 'Close project GitHub origin' })

describe('ProjectGitHubOriginPanel', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  afterEach(() => {
    cleanup()
  })

  it('is collapsed and reads nothing until the owner explicitly inspects', async () => {
    const get = install((id) => Promise.resolve(wire(id, 'Available', { owner: 'acme', repository: 'widgets' })))
    render(<ProjectGitHubOriginPanel projectId={A} />)

    expect(get).not.toHaveBeenCalled()
    expect(screen.queryByRole('button', { name: /^Inspect/ })).toBeNull()

    await click(show())
    expect(get).not.toHaveBeenCalled()
    expect(screen.getByText('Not inspected.')).toBeTruthy()

    await click(inspect())
    expect(get).toHaveBeenCalledTimes(1)
    expect(get).toHaveBeenCalledWith(A)
  })

  it('states the local-only scope and the absence of any remote authority before anything is read', async () => {
    install((id) => Promise.resolve(wire(id, 'NotConfigured')))
    render(<ProjectGitHubOriginPanel projectId={A} />)

    await click(show())

    const note = screen.getByText(/only the origin URL stored in this repository's own local Git configuration/)
    expect(note.textContent).toMatch(/Global, system, included and worktree settings, URL rewrite rules and a separate push destination are ignored/)
    expect(note.textContent).toMatch(/Nothing contacts GitHub/)
    expect(note.textContent).toMatch(/not a verified remote identity, permission, CI result or publication authority/)
  })

  it('shows an Available target as plain text with the time and no link, input or other action', async () => {
    install((id) => Promise.resolve(wire(id, 'Available', { owner: 'acme', repository: 'widgets' })))
    const { container } = render(<ProjectGitHubOriginPanel projectId={A} />)
    await click(show())
    await click(inspect())

    const target = container.querySelector('dl.dc-github-origin-target')!
    expect(target.textContent).toBe('Hostgithub.comOwneracmeRepositorywidgets')
    expect(screen.getByText('Observed at 2026-10-12T09:00:00.000Z (UTC).')).toBeTruthy()
    expect(container.querySelector('a')).toBeNull()
    expect(container.querySelector('input, textarea, select')).toBeNull()
    expect(screen.getAllByRole('button').map((button) => button.textContent)).toEqual(['Inspect again', 'Close project GitHub origin'])
  })

  it('shows NotConfigured and Unavailable as fixed statements, the latter without a time or target', async () => {
    const get = install((id) => Promise.resolve(wire(id, 'NotConfigured')))
    render(<ProjectGitHubOriginPanel projectId={A} />)
    await click(show())
    await click(inspect())

    expect(screen.getByText("No origin URL is configured in this repository's own local Git configuration.")).toBeTruthy()
    expect(screen.getByText(/^Observed at /)).toBeTruthy()

    get.mockResolvedValueOnce(wire(A, 'Unavailable'))
    await click(inspect())

    expect(screen.getByText(/^Unavailable: the repository-local origin could not be observed right now/)).toBeTruthy()
    expect(screen.queryByText(/^Observed at /)).toBeNull()
    expect(screen.queryByText('Host')).toBeNull()
  })

  it('renders a hostile-looking but admitted-shaped value only as inert text and refuses an incoherent answer entirely', async () => {
    const get = install((id) => Promise.resolve(wire(id, 'Available', { owner: 'a-b', repository: 'x.y_z-1' })))
    render(<ProjectGitHubOriginPanel projectId={A} />)
    await click(show())
    await click(inspect())
    expect(screen.getByText('x.y_z-1')).toBeTruthy()

    get.mockResolvedValueOnce(wire(A, 'Available', { owner: '<img src=x onerror=alert(1)>', repository: 'widgets' }))
    await click(inspect())

    expect(screen.getByRole('alert').textContent).toBe('The host answered with a result that is not coherent for this project, so none of it is shown.')
    expect(screen.queryByText(/img src/)).toBeNull()
    expect(screen.queryByText('Host')).toBeNull()
  })

  it('shows a fixed message for a failed read and keeps the explicit actions', async () => {
    install(() => Promise.reject(new Error('C:\\secret\\path boom')))
    render(<ProjectGitHubOriginPanel projectId={A} />)
    await click(show())
    await click(inspect())

    expect(screen.getByRole('alert').textContent).toBe('The project GitHub origin could not be inspected.')
    expect(document.body.textContent).not.toMatch(/secret|boom/)
    expect(screen.getByRole('button', { name: 'Inspect again' })).toBeTruthy()
  })

  it('disables inspecting while a read is pending', async () => {
    install(() => new Promise<ProjectGitHubOriginResponse>(() => undefined))
    render(<ProjectGitHubOriginPanel projectId={A} />)
    await click(show())
    await click(inspect())

    expect(screen.getByText(/^Inspecting the local Git configuration/)).toBeTruthy()
    expect((inspect() as HTMLButtonElement).disabled).toBe(true)
  })

  it('close discards the answer and a reopened region is unchecked and reads nothing', async () => {
    const get = install((id) => Promise.resolve(wire(id, 'Available', { owner: 'acme', repository: 'widgets' })))
    render(<ProjectGitHubOriginPanel projectId={A} />)
    await click(show())
    await click(inspect())
    await click(close())

    expect(screen.queryByText('acme')).toBeNull()
    await click(show())

    expect(screen.getByText('Not inspected.')).toBeTruthy()
    expect(screen.queryByText('acme')).toBeNull()
    expect(get).toHaveBeenCalledTimes(1)
  })

  it('another project starts collapsed and never shows the previous answer, and returning to the first (A-B-A) starts unchecked again', async () => {
    const get = install((id) => Promise.resolve(wire(id, 'Available', { owner: id === A ? 'alpha' : 'beta', repository: 'widgets' })))
    const { rerender } = render(<ProjectGitHubOriginPanel projectId={A} />)
    await click(show())
    await click(inspect())
    expect(screen.getByText('alpha')).toBeTruthy()

    rerender(<ProjectGitHubOriginPanel projectId={B} />)
    expect(screen.queryByText('alpha')).toBeNull()
    expect(screen.queryByRole('button', { name: /^Inspect/ })).toBeNull()
    await click(show())
    await click(inspect())
    expect(screen.getByText('beta')).toBeTruthy()

    rerender(<ProjectGitHubOriginPanel projectId={A} />)
    expect(screen.queryByText('beta')).toBeNull()
    expect(screen.queryByText('alpha')).toBeNull()
    expect(get.mock.calls.map((call) => call[0])).toEqual([A, B])
    await click(show())
    expect(screen.getByText('Not inspected.')).toBeTruthy()
  })

  it('unmounting during a pending read is harmless', async () => {
    let finish!: (value: ProjectGitHubOriginResponse) => void
    install(() => new Promise<ProjectGitHubOriginResponse>((resolve) => { finish = resolve }))
    const errors = vi.spyOn(console, 'error').mockImplementation(() => undefined)
    const { unmount } = render(<ProjectGitHubOriginPanel projectId={A} />)
    await click(show())
    await click(inspect())

    unmount()
    await act(async () => finish(wire(A, 'NotConfigured')))

    expect(errors).not.toHaveBeenCalled()
    errors.mockRestore()
  })

  it('cannot be opened without a project', () => {
    const get = install((id) => Promise.resolve(wire(id, 'NotConfigured')))
    render(<ProjectGitHubOriginPanel projectId={null} />)

    expect((show() as HTMLButtonElement).disabled).toBe(true)
    expect(get).not.toHaveBeenCalled()
  })
})
