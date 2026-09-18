import { renderHook, waitFor } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { processAttemptOutputClient } from '../../../api/clients'
import { useProcessAttemptOutput } from './useProcessAttemptOutput'

vi.mock('../../../api/clients', () => ({
  processAttemptOutputClient: vi.fn(),
}))

function mockClient(getProcessAttemptOutput: ReturnType<typeof vi.fn>) {
  vi.mocked(processAttemptOutputClient).mockReturnValue({
    getProcessAttemptOutput,
  } as unknown as ReturnType<typeof processAttemptOutputClient>)
}

describe('useProcessAttemptOutput', () => {
  it('assembles text across polls and stops once the artifact is sealed and fully drained', async () => {
    const getProcessAttemptOutput = vi
      .fn()
      .mockResolvedValueOnce({ status: 'Ok', text: 'hello ', nextOffset: 6, totalLengthSoFar: 11, isFinal: false, truncated: false })
      .mockResolvedValueOnce({ status: 'Ok', text: 'world', nextOffset: 11, totalLengthSoFar: 11, isFinal: true, truncated: false })
      .mockResolvedValueOnce({ status: 'Ok', text: '', nextOffset: 11, totalLengthSoFar: 11, isFinal: true, truncated: false })
    mockClient(getProcessAttemptOutput)

    const { result } = renderHook(() => useProcessAttemptOutput('run-1', 'attempt-1', 'stdout', 1))

    await waitFor(() => expect(result.current.isFinal).toBe(true))

    expect(result.current.text).toBe('hello world')
    expect(getProcessAttemptOutput).toHaveBeenCalledWith('run-1', 'attempt-1', 'stdout', 0, undefined)
  })

  it('does not poll when runId or attemptId is missing', () => {
    const getProcessAttemptOutput = vi.fn()
    mockClient(getProcessAttemptOutput)

    renderHook(() => useProcessAttemptOutput(null, 'attempt-1', 'stdout'))

    expect(getProcessAttemptOutput).not.toHaveBeenCalled()
  })

  it('surfaces a truncated flag and a non-Ok status from the response', async () => {
    const getProcessAttemptOutput = vi
      .fn()
      .mockResolvedValueOnce({ status: 'IntegrityMismatch', text: '', nextOffset: 0, totalLengthSoFar: 0, isFinal: true, truncated: false })
    mockClient(getProcessAttemptOutput)

    const { result } = renderHook(() => useProcessAttemptOutput('run-1', 'attempt-1', 'stderr', 1))

    await waitFor(() => expect(result.current.isFinal).toBe(true))
    expect(result.current.status).toBe('IntegrityMismatch')
  })

  it('surfaces an error message when the request fails', async () => {
    const getProcessAttemptOutput = vi.fn().mockRejectedValue(new Error('network down'))
    mockClient(getProcessAttemptOutput)

    const { result } = renderHook(() => useProcessAttemptOutput('run-1', 'attempt-1', 'stdout', 1))

    await waitFor(() => expect(result.current.error).toBe('network down'))
  })

  // A recovered-from-interruption artifact reports a genuinely unknown truncation (`null`/
  // `undefined` on the wire) rather than a known true/false. Coalescing that to `false` would
  // be a false claim of "known not truncated", so the hook must preserve it as `undefined`.
  it('preserves an unknown truncation as undefined rather than coalescing it to false', async () => {
    // isFinal only latches once a poll returns no further text (the endpoint's own "caught up"
    // contract) — the second, empty response is what lets the hook settle here.
    const getProcessAttemptOutput = vi
      .fn()
      .mockResolvedValueOnce({ status: 'Ok', text: 'partial', nextOffset: 7, totalLengthSoFar: 7, isFinal: true, truncated: undefined })
      .mockResolvedValueOnce({ status: 'Ok', text: '', nextOffset: 7, totalLengthSoFar: 7, isFinal: true, truncated: undefined })
    mockClient(getProcessAttemptOutput)

    const { result } = renderHook(() => useProcessAttemptOutput('run-1', 'attempt-1', 'stdout', 1))

    await waitFor(() => expect(result.current.isFinal).toBe(true))
    expect(result.current.truncated).toBeUndefined()
  })

  it('preserves a known truncated: false as a definite false, not merely falsy-unknown', async () => {
    const getProcessAttemptOutput = vi
      .fn()
      .mockResolvedValueOnce({ status: 'Ok', text: 'complete', nextOffset: 8, totalLengthSoFar: 8, isFinal: true, truncated: false })
      .mockResolvedValueOnce({ status: 'Ok', text: '', nextOffset: 8, totalLengthSoFar: 8, isFinal: true, truncated: false })
    mockClient(getProcessAttemptOutput)

    const { result } = renderHook(() => useProcessAttemptOutput('run-1', 'attempt-1', 'stdout', 1))

    await waitFor(() => expect(result.current.isFinal).toBe(true))
    expect(result.current.truncated).toBe(false)
  })
})
