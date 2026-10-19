import { act, renderHook } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { useReadingAgeSeconds } from './useReadingAge'

describe('useReadingAgeSeconds', () => {
  beforeEach(() => {
    vi.useFakeTimers()
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  it('has no age without a reading and starts no timer for it', () => {
    const { result } = renderHook(() => useReadingAgeSeconds(null, true))

    expect(result.current).toBeNull()
    expect(vi.getTimerCount()).toBe(0)
  })

  it('ages a watched reading by the browser clock, one whole second at a time, and never below zero', () => {
    const received = performance.now()
    const { result } = renderHook(() => useReadingAgeSeconds(received, true))
    expect(result.current).toBe(0)

    act(() => {
      vi.advanceTimersByTime(4000)
    })
    expect(result.current).toBe(4)

    act(() => {
      vi.advanceTimersByTime(11_000)
    })
    expect(result.current).toBe(15)
  })

  it('does not tick for a reading that is not watched (a terminal attempt) and stops its timer on unmount', () => {
    const received = performance.now()
    const idle = renderHook(() => useReadingAgeSeconds(received, false))
    expect(vi.getTimerCount()).toBe(0)
    idle.unmount()

    const watched = renderHook(() => useReadingAgeSeconds(received, true))
    expect(vi.getTimerCount()).toBe(1)
    watched.unmount()
    expect(vi.getTimerCount()).toBe(0)
  })
})
