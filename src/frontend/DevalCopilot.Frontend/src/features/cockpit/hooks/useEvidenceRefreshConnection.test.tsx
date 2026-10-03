import { act, renderHook } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { useEvidenceRefreshConnection } from './useEvidenceRefreshConnection'

type Props = { projectId: string | null; runId: string | null }

function connection(initial: Props) {
  const frames: { props: Props; generation: number }[] = []
  const rendered = renderHook(
    (props: Props) => {
      const result = useEvidenceRefreshConnection(props.projectId, props.runId)
      frames.push({ props, generation: result.generation })
      return result
    },
    { initialProps: initial },
  )
  return { ...rendered, frames }
}

describe('useEvidenceRefreshConnection', () => {
  it('starts at zero and advances once per request of the current project and run', () => {
    const { result } = connection({ projectId: 'project-a', runId: 'run-1' })
    expect(result.current.generation).toBe(0)

    act(() => result.current.request())
    act(() => result.current.request())

    expect(result.current.generation).toBe(2)
  })

  it('starts the replacement run of the same project at zero, in the very first committed frame, and ignores the old run\'s callback', () => {
    const { result, rerender, frames } = connection({ projectId: 'project-a', runId: 'run-1' })
    const retained = result.current.request
    act(() => retained())
    expect(result.current.generation).toBe(1)

    rerender({ projectId: 'project-a', runId: 'run-2' })
    const afterReplacement = frames.filter((frame) => frame.props.runId === 'run-2')
    expect(afterReplacement.every((frame) => frame.generation === 0)).toBe(true)

    act(() => retained())
    expect(result.current.generation).toBe(0)
    act(() => result.current.request())
    expect(result.current.generation).toBe(1)
  })

  it('never carries a generation across project A to B to A, and an old callback starts nothing for the returning A', () => {
    const { result, rerender, frames } = connection({ projectId: 'project-a', runId: 'run-a' })
    const firstA = result.current.request
    act(() => firstA())
    act(() => firstA())
    expect(result.current.generation).toBe(2)

    rerender({ projectId: 'project-b', runId: 'run-b' })
    expect(result.current.generation).toBe(0)
    act(() => firstA())
    expect(result.current.generation).toBe(0)

    rerender({ projectId: 'project-a', runId: 'run-a' })
    expect(frames.at(-1)?.generation).toBe(0)
    act(() => firstA())
    expect(result.current.generation).toBe(0)
    act(() => result.current.request())
    expect(result.current.generation).toBe(1)
  })

  it('owns nothing while no project is selected, and for a project without a run', () => {
    const none = connection({ projectId: null, runId: null })
    act(() => none.result.current.request())
    expect(none.result.current.generation).toBe(0)

    const noRun = connection({ projectId: 'project-a', runId: null })
    act(() => noRun.result.current.request())
    expect(noRun.result.current.generation).toBe(1)
    noRun.rerender({ projectId: 'project-a', runId: 'run-1' })
    expect(noRun.result.current.generation).toBe(0)
  })

  it('does nothing for a callback retained past unmount', () => {
    const { result, unmount } = connection({ projectId: 'project-a', runId: 'run-1' })
    const retained = result.current.request

    unmount()

    expect(() => retained()).not.toThrow()
  })
})
