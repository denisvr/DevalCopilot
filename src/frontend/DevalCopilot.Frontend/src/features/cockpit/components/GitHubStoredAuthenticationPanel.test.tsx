import { act, cleanup, fireEvent, render, screen } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { gitHubStoredAuthenticationClient } from '../../../api/clients'
import type { GitHubStoredAuthenticationResponse } from '../../../api/clients'
import { GitHubStoredAuthenticationPanel } from './GitHubStoredAuthenticationPanel'

vi.mock('../../../api/clients', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../../api/clients')>()),
  gitHubStoredAuthenticationClient: vi.fn(),
}))

const AT = new Date('2026-10-12T09:00:00.000Z')

const answer = (status: string, observed: Date | null = AT) =>
  ({ host: 'github.com', context: 'StoredDefaultConfig', status, observedAtUtc: observed ?? undefined }) as unknown as GitHubStoredAuthenticationResponse

function install(read: () => Promise<GitHubStoredAuthenticationResponse>) {
  const getGitHubStoredAuthentication = vi.fn(read)
  vi.mocked(gitHubStoredAuthenticationClient).mockReturnValue(
    { getGitHubStoredAuthentication } as unknown as ReturnType<typeof gitHubStoredAuthenticationClient>,
  )
  return getGitHubStoredAuthentication
}

async function click(element: HTMLElement) {
  await act(async () => {
    fireEvent.click(element)
  })
}

const show = () => screen.getByRole('button', { name: 'Show GitHub stored authentication' })
const checkButton = () => screen.getByRole('button', { name: /^Check (stored authentication|again)$/ })
const close = () => screen.getByRole('button', { name: 'Close GitHub stored authentication' })

describe('GitHubStoredAuthenticationPanel', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  afterEach(() => {
    cleanup()
  })

  it('is collapsed and reads nothing until the owner explicitly checks', async () => {
    const get = install(() => Promise.resolve(answer('Authenticated')))
    render(<GitHubStoredAuthenticationPanel />)

    expect(get).not.toHaveBeenCalled()
    expect(screen.queryByRole('button', { name: /^Check/ })).toBeNull()

    await click(show())
    expect(get).not.toHaveBeenCalled()
    expect(screen.getByText('Not checked.')).toBeTruthy()
    expect(screen.getByText(/environment and a custom CLI configuration are not observed/)).toBeTruthy()
    expect(screen.getByText(/neither repository permission nor publication authority/)).toBeTruthy()

    await click(checkButton())
    expect(get).toHaveBeenCalledTimes(1)
  })

  it.each([
    ['CliUnavailable', null, 'The GitHub CLI is not currently available as a vetted direct executable, so nothing was checked.'],
    ['Unknown', null, 'Unknown: the stored authentication could not be determined.'],
    ['NoActiveAccount', AT, 'No active stored account was reported for github.com.'],
    ['Authenticated', AT, 'An active stored account is authenticated for github.com.'],
    ['IssueReported', AT, 'The GitHub CLI reports an authentication issue for the active stored account of github.com.'],
  ])('shows the fixed statement for %s and a time only when observed', async (status, observed, statement) => {
    install(() => Promise.resolve(answer(status, observed)))
    render(<GitHubStoredAuthenticationPanel />)
    await click(show())

    await click(checkButton())

    expect(await screen.findByText(statement)).toBeTruthy()
    if (observed) {
      expect(screen.getByText('Observed at 2026-10-12T09:00:00.000Z (UTC).')).toBeTruthy()
    } else {
      expect(screen.queryByText(/Observed at/)).toBeNull()
    }
    expect(screen.getByRole('button', { name: 'Check again' })).toBeTruthy()
  })

  it('shows loading and failure states, and no earlier answer while rechecking or after a failure', async () => {
    let settle!: (value: GitHubStoredAuthenticationResponse) => void
    const get = install(() => Promise.resolve(answer('Authenticated')))
    render(<GitHubStoredAuthenticationPanel />)
    await click(show())
    await click(checkButton())
    expect(await screen.findByText('An active stored account is authenticated for github.com.')).toBeTruthy()

    get.mockReturnValueOnce(new Promise((resolve) => { settle = resolve }))
    await click(checkButton())
    expect(screen.getByText('Checking stored authentication…')).toBeTruthy()
    expect(screen.queryByText('An active stored account is authenticated for github.com.')).toBeNull()
    expect((screen.getByRole('button', { name: 'Check stored authentication' }) as HTMLButtonElement).disabled).toBe(true)
    await act(async () => settle(answer('Unknown', null)))

    get.mockRejectedValueOnce(new Error('boom'))
    await click(checkButton())
    expect((await screen.findByRole('alert')).textContent).toBe('The stored authentication could not be checked.')
    expect(screen.queryByText(/Unknown: the stored/)).toBeNull()

    get.mockResolvedValueOnce(answer('Authenticated', null))
    await click(checkButton())
    expect((await screen.findByRole('alert')).textContent).toMatch(/not coherent/)
  })

  it('closing discards a read in flight and reopening starts unchecked', async () => {
    let settle!: (value: GitHubStoredAuthenticationResponse) => void
    install(() => new Promise((resolve) => { settle = resolve }))
    render(<GitHubStoredAuthenticationPanel />)
    await click(show())
    await click(checkButton())
    await click(close())

    await click(show())
    await act(async () => settle(answer('Authenticated')))

    expect(screen.getByText('Not checked.')).toBeTruthy()
    expect(screen.queryByText(/authenticated for github.com/)).toBeNull()
  })

  it('renders only fixed plain text, never provider or response text', async () => {
    install(() => Promise.resolve({
      ...answer('Authenticated'),
      login: '<img src=x onerror=alert(1)>',
      token: 'ghp_sentinel',
    } as unknown as GitHubStoredAuthenticationResponse))
    const { container } = render(<GitHubStoredAuthenticationPanel />)
    await click(show())
    await click(checkButton())

    await screen.findByText('An active stored account is authenticated for github.com.')

    expect(container.innerHTML).not.toContain('ghp_sentinel')
    expect(container.querySelector('img')).toBeNull()
  })

  it('unmounting while pending leaves the late answer inert', async () => {
    let settle!: (value: GitHubStoredAuthenticationResponse) => void
    install(() => new Promise((resolve) => { settle = resolve }))
    const errors = vi.spyOn(console, 'error').mockImplementation(() => undefined)
    const { unmount } = render(<GitHubStoredAuthenticationPanel />)
    await click(show())
    await click(checkButton())

    unmount()
    await act(async () => settle(answer('Authenticated')))

    expect(errors).not.toHaveBeenCalled()
    errors.mockRestore()
  })
})
