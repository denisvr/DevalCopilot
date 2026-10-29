// @vitest-environment jsdom
import { act, fireEvent, render, screen } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import {
  ABANDONMENT_EVENT,
  ABANDONMENT_PROJECT,
  ABANDONMENT_REASON,
  ABANDONMENT_RUN,
  notRecordedRunAbandonmentInspectionFor,
  recordedRunAbandonmentInspectionFor,
  unavailableRunAbandonmentInspectionFor,
} from '../runAbandonmentInspectionFixture'
import { RunAbandonmentInspectionRegion } from './RunAbandonmentInspectionRegion'

// RABI-R1 (review 1 of RUN-ABANDON-INSPECT-01): what the open region tells the owner in every state. The common notes are visible while
// the read is pending, after it failed or timed out and beside NotRecorded and Unavailable, so nothing shown in those states may say
// that this run was abandoned, that its objective ended, or that recorded facts exist and agree. Unavailable is also answered for a
// never-abandoned run whose stored mode is unrecognized (both abandonment columns NULL and no event), so it may assert neither that a
// decision was recorded nor that none was. Only Recorded states the historical abandonment. Every expectation reads the rendered text of
// the real region over the real generated client and a fake network, with literal patterns rather than the imported copy constants.

vi.mock('../../../api/session', () => ({ getLaunchSession: () => ({ baseUrl: 'http://127.0.0.1:5099', secret: 'not-a-real-session' }) }))

const URL_OF_RUN = `http://127.0.0.1:5099/api/runs/${ABANDONMENT_RUN}/abandonment-inspection`
const json = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })

/** Statements that only a proved Recorded answer may make about this run. */
const AFFIRMATIVE_ABANDONMENT = [
  /ended the objective/i,
  /ended this run/i,
  /without successful completion/i,
  /was not a successful completion/i,
  /explicit abandonment/i,
  /agree with each other/i,
  /internally consistent/i,
  /facts are recorded/i,
  /abandonment (is|was) recorded for this run/i,
]

/** Statements that a decision is absent; Unavailable may not make them either. */
const ABSENCE = [/found no recorded abandonment/i, /no abandonment (is|was) recorded/i, /never abandoned/i, /was not abandoned/i]

function serve(answer: () => Promise<Response>) {
  vi.spyOn(window, 'fetch').mockImplementation((input) => (String(input) === URL_OF_RUN ? answer() : Promise.resolve(json({}, 404))))
}

function open() {
  const view = render(<RunAbandonmentInspectionRegion runId={ABANDONMENT_RUN} projectId={ABANDONMENT_PROJECT} />)
  fireEvent.click(screen.getByRole('button', { name: 'Inspect recorded abandonment' }))
  return view
}

const region = () => screen.getByRole('region', { name: 'Inspect recorded abandonment' })

function expectNoAffirmativeAbandonment() {
  const text = region().textContent ?? ''
  for (const pattern of AFFIRMATIVE_ABANDONMENT) {
    expect(text, `must not claim ${pattern}`).not.toMatch(pattern)
  }

  expect(text).not.toContain(ABANDONMENT_REASON)
  expect(text).not.toContain(ABANDONMENT_EVENT)
}

function expectCommonNotesVisible() {
  const text = region().textContent ?? ''
  expect(text).toMatch(/shows a Human abandonment only if the host recorded one for this run and every fact of it can be proved/)
  expect(text).toMatch(/proves no cancellation, workspace repair, ownership release, current permission to record another objective, or publication/)
  expect(text).toMatch(/separate reads, not one atomic replay/)
}

describe('RunAbandonmentInspectionRegion copy semantics (RABI-R1)', () => {
  beforeEach(() => {
    vi.restoreAllMocks()
  })
  afterEach(() => {
    vi.useRealTimers()
    vi.restoreAllMocks()
  })

  it('while the read is pending, the common notes are visible and nothing says this run was abandoned', async () => {
    serve(() => new Promise<Response>(() => undefined))
    open()

    expect(await screen.findByText(/Reading what the host recorded for this run/)).toBeInTheDocument()
    expectCommonNotesVisible()
    expectNoAffirmativeAbandonment()
    expect(region().textContent).not.toMatch(/the recorded abandonment of this run/i)
  })

  it('after a failed read, the common notes are visible and nothing says this run was abandoned', async () => {
    serve(() => Promise.reject(new Error('network down')))
    open()

    expect(await screen.findByRole('alert')).toHaveTextContent(/This read failed, so nothing is shown/)
    expectCommonNotesVisible()
    expectNoAffirmativeAbandonment()
  })

  it('after the ten-second timeout, the common notes are visible and nothing says this run was abandoned', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: false })
    serve(() => new Promise<Response>(() => undefined))
    open()

    await act(async () => {
      await vi.advanceTimersByTimeAsync(10_000)
    })

    expect(screen.getByRole('alert')).toHaveTextContent(/did not answer in time, so nothing is shown/)
    expectCommonNotesVisible()
    expectNoAffirmativeAbandonment()
  })

  it('for an answer that is not coherent for this run, nothing says a recorded abandonment was answered', async () => {
    serve(() => Promise.resolve(json(recordedRunAbandonmentInspectionFor({}, { eventSequence: '0' }))))
    open()

    expect(await screen.findByRole('alert')).toHaveTextContent(/not coherent for this run, so none of it is shown/)
    expectCommonNotesVisible()
    expectNoAffirmativeAbandonment()
    expect(region().textContent).not.toMatch(/answered with a recorded abandonment/i)
  })

  it('for a newer run that was never abandoned (NotRecorded), nothing says its objective ended through abandonment', async () => {
    serve(() => Promise.resolve(json(notRecordedRunAbandonmentInspectionFor())))
    open()

    expect(await screen.findByRole('status')).toHaveTextContent(/This read found no recorded abandonment for this run/)
    expect(screen.getByRole('status')).toHaveTextContent(/says nothing about whether this run can be abandoned now, how it ended/)
    expectCommonNotesVisible()
    expectNoAffirmativeAbandonment()
    expect(screen.queryByText(/Abandonment ended the objective of this run without successful completion/)).toBeNull()
  })

  it('for Unavailable (also answered for a never-abandoned run with an unrecognized mode), nothing asserts that a decision exists or that none does', async () => {
    serve(() => Promise.resolve(json(unavailableRunAbandonmentInspectionFor())))
    open()

    const status = await screen.findByRole('status')
    expect(status).toHaveTextContent(/could not be proved as a whole, so nothing is shown/)
    expect(status).toHaveTextContent(/neither that an abandonment was recorded nor that none was, nor how the run ended/)
    expectCommonNotesVisible()
    expectNoAffirmativeAbandonment()
    for (const pattern of ABSENCE) {
      expect(region().textContent, `must not claim ${pattern}`).not.toMatch(pattern)
    }

    expect(screen.queryByText(/^Abandonment facts are recorded for this run/)).toBeNull()
  })

  it('positive control: only a proved Recorded answer states the historical abandonment, its consistency and its facts', async () => {
    serve(() => Promise.resolve(json(recordedRunAbandonmentInspectionFor())))
    open()

    expect(await screen.findByText('Recorded reason')).toBeInTheDocument()
    const text = region().textContent ?? ''
    expect(text).toMatch(/A Human ended this run by explicit abandonment/)
    expect(text).toMatch(/The abandonment ended the objective of this run without successful completion/)
    expect(text).toMatch(/These facts agree with each other within one database read; they are not signed or authenticated history/)
    expect(text).toContain(ABANDONMENT_REASON)
    expect(text).toContain(`${ABANDONMENT_EVENT} · sequence 42`)
    expectCommonNotesVisible()
    for (const pattern of ABSENCE) {
      expect(text, `must not claim ${pattern}`).not.toMatch(pattern)
    }
  })
})
