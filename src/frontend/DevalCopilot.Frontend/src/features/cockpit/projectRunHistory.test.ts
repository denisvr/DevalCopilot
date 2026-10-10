import { describe, expect, it } from 'vitest'
import { GetProjectRunHistoryResponse } from '../../api/generated/api-client'
import { toRunHistoryPage } from './projectRunHistory'
import type { RunHistoryRequest } from './projectRunHistory'
import { HISTORY_PROJECT, deliveredEntryFor, descending, entryFor, pageOf, runIdFor, sourceResponseFor } from './projectRunHistoryFixture'

const first: RunHistoryRequest = { projectId: HISTORY_PROJECT, beforeExecutionNumber: null, limit: 10 }

const accepted = (response: GetProjectRunHistoryResponse | null | undefined, request: RunHistoryRequest = first, retained = [] as never[]) =>
  toRunHistoryPage(response, request, retained)

describe('toRunHistoryPage', () => {
  it('accepts a coherent descending page and keeps only the recorded values', () => {
    const page = accepted(pageOf(descending(12, 3), { hasMore: true }))

    expect(page).not.toBeNull()
    expect(page?.entries.map((entry) => entry.executionNumber)).toEqual(descending(12, 3))
    expect(page?.hasMore).toBe(true)
    expect(page?.nextBeforeExecutionNumber).toBe(3)
    expect(page?.entries[0]).toEqual({
      projectId: HISTORY_PROJECT,
      runId: 'run-12',
      executionNumber: 12,
      objective: 'Objective 12',
      lifecycle: 'Created',
      stage: 'Intake',
      executionMode: 'ManualAgent',
      createdAtUtc: expect.any(Date),
      lastAdvancedAtUtc: expect.any(Date),
      receiptSource: null,
    })
  })

  it('accepts an empty final page', () => {
    expect(accepted(pageOf([]))).toEqual({ entries: [], hasMore: false, nextBeforeExecutionNumber: null })
  })

  it('accepts a page that exactly fills the limit when no older run remains', () => {
    const page = accepted(pageOf(descending(10, 1)))

    expect(page?.entries).toHaveLength(10)
    expect(page?.hasMore).toBe(false)
    expect(page?.nextBeforeExecutionNumber).toBeNull()
  })

  it('keeps every fixed Unrecognized disclosure and every known lifecycle, stage and mode', () => {
    const lifecycles = ['Created', 'Running', 'Completed', 'Failed', 'Interrupted', 'Abandoned', 'Unrecognized']
    const stages = ['Intake', 'Plan', 'Critique', 'Resolution', 'Execute', 'Completed', 'Unrecognized']
    const modes = ['Legacy', 'Simulated', 'ManualAgent', 'Unrecognized']
    const entries = lifecycles.map((lifecycle, index) =>
      entryFor(20 - index, { lifecycle, stage: stages[index], executionMode: modes[index % modes.length] }),
    )

    const page = accepted(pageOf([], { entries }))

    expect(page?.entries.map((entry) => entry.lifecycle)).toEqual(lifecycles)
    expect(page?.entries.map((entry) => entry.stage)).toEqual(stages)
    expect(page?.entries.map((entry) => entry.executionMode)).toEqual(lifecycles.map((_, index) => modes[index % modes.length]))
  })

  it('maps a located source for a Completed run and keeps a missing one as null', () => {
    const page = accepted(pageOf([], { entries: [deliveredEntryFor(5), entryFor(4, { lifecycle: 'Completed', stage: 'Completed' })] }))

    expect(page?.entries[0].receiptSource).toEqual({
      runId: 'run-5',
      operationId: 'operation-5',
      commitSha: 'c'.repeat(40),
      checkpointId: 'checkpoint-5',
      checkpointNumber: 3,
    })
    expect(page?.entries[1].receiptSource).toBeNull()
  })

  it.each([
    ['no response', null],
    ['an undefined response', undefined],
    ['a response without entries', new GetProjectRunHistoryResponse({ projectId: HISTORY_PROJECT, hasMore: false })],
    ['a response of another project', pageOf([3, 2], { projectId: 'project-2' })],
    ['a response without a project', pageOf([3, 2], { projectId: '' })],
    ['an entry of another project', pageOf([], { entries: [entryFor(3), entryFor(2, { projectId: 'project-2' })] })],
    ['more entries than were requested', pageOf(descending(11, 1))],
    ['a repeated run identity', pageOf([], { entries: [entryFor(3), entryFor(2, { runId: runIdFor(3) })] })],
    ['a repeated execution number', pageOf([], { entries: [entryFor(3), entryFor(3, { runId: 'run-other' })] })],
    ['an ascending order', pageOf([2, 3])],
    ['an unordered page', pageOf([5, 3, 4])],
    ['a zero execution number', pageOf([], { entries: [entryFor(1), entryFor(0)] })],
    ['a negative execution number', pageOf([], { entries: [entryFor(-1)] })],
    ['a fractional execution number', pageOf([], { entries: [entryFor(2.5)] })],
    ['a non-finite execution number', pageOf([], { entries: [entryFor(Number.NaN)] })],
    ['an unsafe execution number', pageOf([], { entries: [entryFor(Number.MAX_SAFE_INTEGER + 2)] })],
    ['a blank run identity', pageOf([], { entries: [entryFor(3, { runId: '  ' })] })],
    ['a blank objective', pageOf([], { entries: [entryFor(3, { objective: '   ' })] })],
    ['an unknown lifecycle', pageOf([], { entries: [entryFor(3, { lifecycle: 'Archived' })] })],
    ['an unknown stage', pageOf([], { entries: [entryFor(3, { stage: 'Nowhere' })] })],
    ['an unknown execution mode', pageOf([], { entries: [entryFor(3, { executionMode: 'Weird' })] })],
    ['a missing lifecycle', pageOf([], { entries: [entryFor(3, { lifecycle: undefined })] })],
    ['an invalid creation time', pageOf([], { entries: [entryFor(3, { createdAtUtc: new Date(Number.NaN) })] })],
    ['a missing last-advance time', pageOf([], { entries: [entryFor(3, { lastAdvancedAtUtc: undefined })] })],
    ['more with no next cursor', pageOf(descending(10, 1), { hasMore: true, next: undefined })],
    ['more with a cursor that is not the last number', pageOf(descending(10, 1), { hasMore: true, next: 2 })],
    ['more with a non-positive cursor', pageOf(descending(10, 1), { hasMore: true, next: 0 })],
    ['more on a page shorter than the limit', pageOf(descending(9, 1), { hasMore: true })],
    ['more on an empty page', pageOf([], { hasMore: true, next: 1 })],
    ['a cursor without more', pageOf(descending(3, 1), { hasMore: false, next: 1 })],
  ])('refuses %s as a whole, never as a partial page', (_name, response) => {
    expect(accepted(response)).toBeNull()
  })

  it.each([
    ['another run', deliveredEntryFor(5, { receiptSource: sourceResponseFor(5, { runId: 'run-4' }) })],
    ['a run that is not Completed', entryFor(5, { receiptSource: sourceResponseFor(5) })],
    ['a run that is Completed but not at the Completed stage', deliveredEntryFor(5, { stage: 'Execute' })],
    ['an unrecognized lifecycle', deliveredEntryFor(5, { lifecycle: 'Unrecognized' })],
    ['a blank operation', deliveredEntryFor(5, { receiptSource: sourceResponseFor(5, { operationId: '' }) })],
    ['a short commit', deliveredEntryFor(5, { receiptSource: sourceResponseFor(5, { commitSha: 'c'.repeat(39) }) })],
    ['an uppercase commit', deliveredEntryFor(5, { receiptSource: sourceResponseFor(5, { commitSha: 'C'.repeat(40) }) })],
    ['a blank checkpoint', deliveredEntryFor(5, { receiptSource: sourceResponseFor(5, { checkpointId: ' ' }) })],
    ['a zero checkpoint number', deliveredEntryFor(5, { receiptSource: sourceResponseFor(5, { checkpointNumber: 0 }) })],
    ['a fractional checkpoint number', deliveredEntryFor(5, { receiptSource: sourceResponseFor(5, { checkpointNumber: 1.5 }) })],
    ['an unsafe checkpoint number', deliveredEntryFor(5, { receiptSource: sourceResponseFor(5, { checkpointNumber: 2 ** 60 }) })],
  ])('refuses the whole page when a source names %s', (_name, entry) => {
    expect(accepted(pageOf([], { entries: [entry] }))).toBeNull()
  })

  it('checks the page against the cursor it was requested with', () => {
    const request: RunHistoryRequest = { projectId: HISTORY_PROJECT, beforeExecutionNumber: 10, limit: 10 }

    expect(accepted(pageOf(descending(9, 5)), request)?.entries).toHaveLength(5)
    expect(accepted(pageOf(descending(10, 5)), request)).toBeNull()
    expect(accepted(pageOf(descending(12, 8)), request)).toBeNull()
    expect(accepted(pageOf([]), request)).toEqual({ entries: [], hasMore: false, nextBeforeExecutionNumber: null })
  })

  it('checks the page against the rows already retained, so a page can neither repeat nor interleave them', () => {
    const request: RunHistoryRequest = { projectId: HISTORY_PROJECT, beforeExecutionNumber: 20, limit: 10 }
    const retained = accepted(pageOf(descending(29, 20), { hasMore: true }))!.entries

    expect(toRunHistoryPage(pageOf(descending(19, 15)), request, retained)?.entries).toHaveLength(5)
    expect(toRunHistoryPage(pageOf([], { entries: [entryFor(19, { runId: retained[3].runId })] }), request, retained)).toBeNull()
    expect(toRunHistoryPage(pageOf([], { entries: [entryFor(25), entryFor(19)] }), request, retained)).toBeNull()
  })

  it('takes the answered page bound from the request, never a fixed constant', () => {
    expect(accepted(pageOf(descending(3, 1)), { ...first, limit: 2 })).toBeNull()
    expect(accepted(pageOf(descending(2, 1)), { ...first, limit: 2 })).not.toBeNull()
    expect(accepted(pageOf(descending(2, 1), { hasMore: true }), { ...first, limit: 2 })?.nextBeforeExecutionNumber).toBe(1)
  })
})
