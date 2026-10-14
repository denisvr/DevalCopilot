import { describe, expect, it } from 'vitest'
import type { GetGitHubCheckRunOutputObservationResponse } from '../../api/clients'
import {
  CHECK_RUN_OUTPUT_STATEMENTS,
  MAXIMUM_OUTPUT_BYTES,
  MAXIMUM_SUMMARY_BYTES,
  MAXIMUM_TEXT_BYTES,
  MAXIMUM_TITLE_BYTES,
  checkRunOutputScopeKey,
  toCheckRunOutputObservation,
} from './gitHubCheckRunOutputObservation'
import type { CheckRunOutputScope } from './gitHubCheckRunOutputObservation'
import type { ObservationScope, ObservationSource } from './gitHubObservationTarget'

const RUN = '11111111-1111-1111-1111-111111111111'
const OPERATION = '22222222-2222-2222-2222-222222222222'
const SHA = 'a'.repeat(40)
const OTHER_SHA = 'b'.repeat(40)
const BRANCH = 'devalcopilot/workspace/abc/1'
const NODE_ID = 'R_kgDOExample01'
const CHECK = 'CR_kwDOCheck001'
const SUITE = 'CS_kwDOSuite001'
const RECORDED = new Date('2026-10-12T20:00:00.000Z')
const BOUND_OBSERVED = new Date('2026-10-12T20:30:00.000Z')
const BOUND_RECORDED = new Date('2026-10-12T20:31:00.000Z')
const OBSERVED = new Date('2026-10-12T21:00:00.000Z')

// Every special character is built from its code point, so the source stays ASCII and no tool can rewrite it.
const ch = (codePoint: number) => String.fromCodePoint(codePoint)
const lone = (unit: number) => String.fromCharCode(unit)

const source: ObservationSource = { runId: RUN, operationId: OPERATION, commitSha: SHA, branchName: BRANCH }
const target: ObservationScope = {
  id: '33333333-3333-3333-3333-333333333333',
  eventId: '44444444-4444-4444-4444-444444444444',
  host: 'github.com',
  owner: 'octo-cat',
  repository: 'hello.world',
  recordedAtUtc: RECORDED,
}
const scope: CheckRunOutputScope = { source, target, checkRunId: CHECK }

const wireSource = (overrides: Record<string, unknown> = {}) => ({ operationId: OPERATION, commitSha: SHA, branchName: BRANCH, ...overrides })
const wireTarget = (overrides: Record<string, unknown> = {}) => ({
  id: target.id,
  eventId: target.eventId,
  host: 'github.com',
  owner: 'octo-cat',
  repository: 'hello.world',
  recordedAtUtc: RECORDED,
  ...overrides,
})
const wireBinding = (overrides: Record<string, unknown> = {}) => ({
  id: '55555555-5555-5555-5555-555555555555',
  eventId: '66666666-6666-6666-6666-666666666666',
  repositoryNodeId: NODE_ID,
  owner: 'octo-cat',
  repository: 'hello.world',
  observedAtUtc: BOUND_OBSERVED,
  recordedAtUtc: BOUND_RECORDED,
  ...overrides,
})
const wireRef = (overrides: Record<string, unknown> = {}) => ({ branchName: BRANCH, commitSha: SHA, ...overrides })
const wireOutput = (overrides: Record<string, unknown> = {}) => ({ title: 'Title', summary: 'Summary', text: 'Text', ...overrides })
const wireCheckRun = (overrides: Record<string, unknown> = {}) => ({
  id: CHECK,
  checkSuiteId: SUITE,
  commitSha: SHA,
  name: 'build',
  status: 'COMPLETED',
  conclusion: 'FAILURE',
  output: wireOutput(),
  ...overrides,
})
const wireObservation = (overrides: Record<string, unknown> = {}) => ({
  repositoryNodeId: NODE_ID,
  owner: 'octo-cat',
  repository: 'hello.world',
  observedAtUtc: OBSERVED,
  ref: wireRef(),
  checkRun: null,
  ...overrides,
})
const response = (overrides: Record<string, unknown>) =>
  ({ runId: RUN, checkRunId: CHECK, ...overrides }) as unknown as GetGitHubCheckRunOutputObservationResponse
const full = (status: string, observation: unknown, binding: unknown = wireBinding()) =>
  response({ status, source: wireSource(), target: wireTarget(), binding, observation })
const reported = (overrides: Record<string, unknown> = {}, output: Record<string, unknown> = {}) =>
  full('OutputReported', wireObservation({ checkRun: wireCheckRun({ output: wireOutput(output), ...overrides }) }))
const notReturned = (overrides: Record<string, unknown> = {}) =>
  full('OutputNotReturned', wireObservation({ checkRun: wireCheckRun({ output: null, ...overrides }) }))
const different = (overrides: Record<string, unknown> = {}) =>
  full('CommitDifferent', wireObservation({ ref: wireRef({ commitSha: OTHER_SHA }), ...overrides }))
const unavailable = (overrides: Record<string, unknown> = {}) => full('CheckRunUnavailable', wireObservation(overrides))
const noRef = (overrides: Record<string, unknown> = {}) => full('RefNotReturned', wireObservation({ ref: null, ...overrides }))

const FACTS = { repositoryNodeId: NODE_ID, owner: 'octo-cat', repository: 'hello.world', observedAtUtc: OBSERVED }
const BINDING = {
  id: '55555555-5555-5555-5555-555555555555',
  eventId: '66666666-6666-6666-6666-666666666666',
  repositoryNodeId: NODE_ID,
  owner: 'octo-cat',
  repository: 'hello.world',
  observedAtUtc: BOUND_OBSERVED,
  recordedAtUtc: BOUND_RECORDED,
}
const REF = { branchName: BRANCH, commitSha: SHA }
const CHECK_RUN = { id: CHECK, checkSuiteId: SUITE, commitSha: SHA, name: 'build', status: 'COMPLETED', conclusion: 'FAILURE' }

describe('toCheckRunOutputObservation', () => {
  it('accepts exactly the eleven closed statuses with exactly their documented facts', () => {
    expect(toCheckRunOutputObservation(response({ status: 'NotRecorded', source: null, target: null, binding: null, observation: null }), scope)).toEqual({ status: 'NotRecorded' })
    expect(toCheckRunOutputObservation(response({ status: 'Unavailable', source: null, target: null, binding: null, observation: null }), scope)).toEqual({ status: 'Unavailable' })
    expect(toCheckRunOutputObservation(response({ status: 'BindingNotRecorded', source: wireSource(), target: wireTarget(), binding: null, observation: null }), scope)).toEqual({ status: 'BindingNotRecorded' })
    for (const status of ['CliUnavailable', 'Unknown', 'IdentityChanged'] as const) {
      expect(toCheckRunOutputObservation(full(status, null), scope)).toEqual({ status, binding: BINDING })
    }

    expect(toCheckRunOutputObservation(noRef(), scope)).toEqual({ status: 'RefNotReturned', binding: BINDING, facts: FACTS })
    expect(toCheckRunOutputObservation(different(), scope)).toEqual({
      status: 'CommitDifferent',
      binding: BINDING,
      facts: FACTS,
      ref: { branchName: BRANCH, commitSha: OTHER_SHA },
    })
    expect(toCheckRunOutputObservation(unavailable(), scope)).toEqual({ status: 'CheckRunUnavailable', binding: BINDING, facts: FACTS, ref: REF })
    expect(toCheckRunOutputObservation(notReturned(), scope)).toEqual({
      status: 'OutputNotReturned',
      binding: BINDING,
      facts: FACTS,
      ref: REF,
      checkRun: CHECK_RUN,
    })
    expect(toCheckRunOutputObservation(reported(), scope)).toEqual({
      status: 'OutputReported',
      binding: BINDING,
      facts: FACTS,
      ref: REF,
      checkRun: CHECK_RUN,
      output: { title: 'Title', summary: 'Summary', text: 'Text' },
    })
  })

  it('refuses a missing response, another run, another or missing check run identifier and an unknown status', () => {
    expect(toCheckRunOutputObservation(null, scope)).toBeNull()
    expect(toCheckRunOutputObservation(undefined, scope)).toBeNull()
    expect(toCheckRunOutputObservation(response({ runId: '99999999-9999-9999-9999-999999999999', status: 'NotRecorded' }), scope)).toBeNull()
    expect(toCheckRunOutputObservation({ ...noRef(), checkRunId: 'CR_other' } as GetGitHubCheckRunOutputObservationResponse, scope)).toBeNull()
    expect(toCheckRunOutputObservation({ ...noRef(), checkRunId: CHECK.toLowerCase() } as GetGitHubCheckRunOutputObservationResponse, scope)).toBeNull()
    expect(toCheckRunOutputObservation({ ...noRef(), checkRunId: undefined } as unknown as GetGitHubCheckRunOutputObservationResponse, scope)).toBeNull()
    for (const status of ['Passed', 'ContextsReported', 'NoContextsReturned', '', undefined, null, 5]) {
      expect(toCheckRunOutputObservation(response({ status, source: null, target: null, binding: null, observation: null }), scope)).toBeNull()
    }
  })

  it('refuses facts beside NotRecorded and Unavailable and a binding or observation beside BindingNotRecorded', () => {
    for (const status of ['NotRecorded', 'Unavailable']) {
      for (const extra of [{ source: wireSource() }, { target: wireTarget() }, { binding: wireBinding() }, { observation: wireObservation() }]) {
        expect(toCheckRunOutputObservation(response({ status, source: null, target: null, binding: null, observation: null, ...extra }), scope)).toBeNull()
      }
    }

    const base = { status: 'BindingNotRecorded', source: wireSource(), target: wireTarget(), binding: null, observation: null }
    expect(toCheckRunOutputObservation(response({ ...base, binding: wireBinding() }), scope)).toBeNull()
    expect(toCheckRunOutputObservation(response({ ...base, observation: wireObservation() }), scope)).toBeNull()
    expect(toCheckRunOutputObservation(response({ ...base, source: null }), scope)).toBeNull()
    expect(toCheckRunOutputObservation(response({ ...base, target: null }), scope)).toBeNull()
  })

  it('requires exactly this source and target for every status that carries them', () => {
    for (const bad of [
      { source: wireSource({ operationId: '99999999-9999-9999-9999-999999999999' }) },
      { source: wireSource({ commitSha: OTHER_SHA }) },
      { source: wireSource({ branchName: 'elsewhere' }) },
      { target: wireTarget({ id: '99999999-9999-9999-9999-999999999999' }) },
      { target: wireTarget({ owner: 'someone-else' }) },
      { target: wireTarget({ repository: 'other.repo' }) },
      { target: wireTarget({ host: 'example.com' }) },
      { target: wireTarget({ recordedAtUtc: new Date('2026-10-12T20:00:01.000Z') }) },
    ]) {
      expect(toCheckRunOutputObservation({ ...reported(), ...bad } as GetGitHubCheckRunOutputObservationResponse, scope)).toBeNull()
      expect(toCheckRunOutputObservation({ ...full('Unknown', null), ...bad } as GetGitHubCheckRunOutputObservationResponse, scope)).toBeNull()
    }
  })

  it('refuses an observation beside CliUnavailable, Unknown and IdentityChanged and a missing binding', () => {
    for (const status of ['CliUnavailable', 'Unknown', 'IdentityChanged']) {
      expect(toCheckRunOutputObservation(full(status, wireObservation()), scope)).toBeNull()
      expect(toCheckRunOutputObservation(full(status, null, null), scope)).toBeNull()
    }
  })

  it('refuses a binding that is not coherent with the recorded target', () => {
    for (const binding of [
      wireBinding({ repositoryNodeId: 'R bad' }),
      wireBinding({ repositoryNodeId: '' }),
      wireBinding({ owner: 'other-owner' }),
      wireBinding({ repository: 'other.repo' }),
      wireBinding({ observedAtUtc: new Date('2026-10-12T19:59:59.000Z') }),
      wireBinding({ recordedAtUtc: new Date('2026-10-12T20:29:59.000Z') }),
      wireBinding({ id: '' }),
      wireBinding({ observedAtUtc: 'not a date' }),
    ]) {
      expect(toCheckRunOutputObservation(full('Unknown', null, binding), scope)).toBeNull()
      expect(toCheckRunOutputObservation(full('OutputReported', wireObservation({ checkRun: wireCheckRun() }), binding), scope)).toBeNull()
    }
  })

  it('accepts names that differ from the recorded ones only in ASCII case and refuses every other difference', () => {
    const ok = reported()
    const observed = (overrides: Record<string, unknown>) =>
      full('OutputReported', wireObservation({ owner: 'OCTO-CAT', repository: 'Hello.World', checkRun: wireCheckRun(), ...overrides }))
    expect(toCheckRunOutputObservation(ok, scope)).not.toBeNull()
    expect(toCheckRunOutputObservation(observed({}), scope)).toMatchObject({ facts: { owner: 'OCTO-CAT', repository: 'Hello.World' } })
    for (const overrides of [
      { owner: 'octo-cat ' },
      { owner: 'other-owner' },
      { repository: 'hello.world.git' },
      { repository: 'hello.worlD2' },
      { owner: ch(0xff2f) + 'cto-cat' },
    ]) {
      expect(toCheckRunOutputObservation(observed(overrides), scope)).toBeNull()
    }
  })

  it('requires the bound repository ID exactly and a time not before the binding', () => {
    for (const overrides of [
      { repositoryNodeId: 'R_other' },
      { repositoryNodeId: NODE_ID.toLowerCase() },
      { repositoryNodeId: null },
      { observedAtUtc: new Date(BOUND_RECORDED.getTime() - 1) },
      { observedAtUtc: null },
      { observedAtUtc: 'later' },
    ]) {
      expect(toCheckRunOutputObservation(full('OutputReported', wireObservation({ checkRun: wireCheckRun(), ...overrides })), scope)).toBeNull()
      expect(toCheckRunOutputObservation(noRef(overrides), scope)).toBeNull()
    }

    expect(toCheckRunOutputObservation(full('OutputReported', wireObservation({ checkRun: wireCheckRun(), observedAtUtc: BOUND_RECORDED })), scope)).not.toBeNull()
  })

  it('RefNotReturned carries no ref and no check run', () => {
    expect(toCheckRunOutputObservation(noRef({ ref: wireRef() }), scope)).toBeNull()
    expect(toCheckRunOutputObservation(noRef({ checkRun: wireCheckRun() }), scope)).toBeNull()
    expect(toCheckRunOutputObservation(noRef({ checkRun: undefined }), scope)).not.toBeNull()
  })

  it('CommitDifferent needs a valid ref of the recorded branch and another commit, and never a check run', () => {
    expect(toCheckRunOutputObservation(different({ checkRun: wireCheckRun({ commitSha: OTHER_SHA }) }), scope)).toBeNull()
    expect(toCheckRunOutputObservation(different({ ref: wireRef() }), scope)).toBeNull()
    expect(toCheckRunOutputObservation(different({ ref: null }), scope)).toBeNull()
    expect(toCheckRunOutputObservation(different({ ref: wireRef({ commitSha: OTHER_SHA, branchName: 'other' }) }), scope)).toBeNull()
    for (const commitSha of ['B'.repeat(40), 'b'.repeat(39), 'b'.repeat(41), 'g'.repeat(40), '', null]) {
      expect(toCheckRunOutputObservation(different({ ref: wireRef({ commitSha }) }), scope)).toBeNull()
    }
  })

  it('CheckRunUnavailable needs the matching ref and never a check run or another commit', () => {
    expect(toCheckRunOutputObservation(unavailable({ checkRun: wireCheckRun() }), scope)).toBeNull()
    expect(toCheckRunOutputObservation(unavailable({ ref: wireRef({ commitSha: OTHER_SHA }) }), scope)).toBeNull()
    expect(toCheckRunOutputObservation(unavailable({ ref: null }), scope)).toBeNull()
    expect(toCheckRunOutputObservation(unavailable({ ref: wireRef({ branchName: 'other' }) }), scope)).toBeNull()
  })

  it('the two output statuses need the matching ref and an admitted check run', () => {
    for (const build of [reported, notReturned]) {
      expect(toCheckRunOutputObservation(build({ commitSha: OTHER_SHA }), scope)).toBeNull()
      expect(toCheckRunOutputObservation(build({ id: 'CR_other' }), scope)).toBeNull()
      expect(toCheckRunOutputObservation(build({ id: CHECK.toLowerCase() }), scope)).toBeNull()
      expect(toCheckRunOutputObservation(build({ checkSuiteId: 'bad id' }), scope)).toBeNull()
      expect(toCheckRunOutputObservation(build({ checkSuiteId: null }), scope)).toBeNull()
      expect(toCheckRunOutputObservation(build({ name: '   ' }), scope)).toBeNull()
      expect(toCheckRunOutputObservation(build({ status: 'DONE' }), scope)).toBeNull()
      expect(toCheckRunOutputObservation(build({ conclusion: 'passed' }), scope)).toBeNull()
      expect(toCheckRunOutputObservation(build({ conclusion: 5 }), scope)).toBeNull()
    }

    expect(toCheckRunOutputObservation(full('OutputReported', wireObservation({ checkRun: wireCheckRun(), ref: wireRef({ commitSha: OTHER_SHA }) })), scope)).toBeNull()
    expect(toCheckRunOutputObservation(full('OutputReported', wireObservation({ checkRun: wireCheckRun(), ref: null })), scope)).toBeNull()
    expect(toCheckRunOutputObservation(full('OutputReported', wireObservation({ checkRun: null })), scope)).toBeNull()
    expect(toCheckRunOutputObservation(full('OutputNotReturned', wireObservation({ checkRun: null })), scope)).toBeNull()
  })

  it('keeps status and conclusion independent: any of six statuses with any of nine conclusions or none', () => {
    const statuses = ['COMPLETED', 'IN_PROGRESS', 'PENDING', 'QUEUED', 'REQUESTED', 'WAITING']
    const conclusions = ['ACTION_REQUIRED', 'CANCELLED', 'FAILURE', 'NEUTRAL', 'SKIPPED', 'STALE', 'STARTUP_FAILURE', 'SUCCESS', 'TIMED_OUT', null]
    for (const status of statuses) {
      for (const conclusion of conclusions) {
        expect(toCheckRunOutputObservation(reported({ status, conclusion }), scope)).toMatchObject({ checkRun: { status, conclusion } })
      }
    }

    for (const status of ['completed', 'SUCCESS', '', ' COMPLETED', null, undefined]) {
      expect(toCheckRunOutputObservation(reported({ status }), scope)).toBeNull()
    }

    for (const conclusion of ['success', 'COMPLETED', '', 'SUCCESS ', undefined]) {
      expect(toCheckRunOutputObservation(reported({ conclusion }), scope)).toBeNull()
    }
  })

  it('OutputReported needs at least one non-null field and OutputNotReturned needs none', () => {
    expect(toCheckRunOutputObservation(reported({}, { title: null, summary: null, text: null }), scope)).toBeNull()
    expect(toCheckRunOutputObservation(reported({ output: null }), scope)).toBeNull()
    expect(toCheckRunOutputObservation(reported({ output: undefined }), scope)).toBeNull()
    expect(toCheckRunOutputObservation(notReturned({ output: wireOutput() }), scope)).toBeNull()
    expect(toCheckRunOutputObservation(notReturned({ output: wireOutput({ title: null, summary: null, text: null }) }), scope)).toBeNull()
    for (const only of [{ title: 't' }, { summary: 's' }, { text: 'x' }]) {
      expect(
        toCheckRunOutputObservation(reported({}, { title: null, summary: null, text: null, ...only }), scope),
      ).toMatchObject({ status: 'OutputReported' })
    }
  })

  it('keeps null, empty, whitespace, tab, line feed and carriage return exactly as returned, never trimmed or normalized', () => {
    const reading = toCheckRunOutputObservation(reported({}, { title: '', summary: '  ', text: 'a\tb\r\nc\nd\re ' }), scope)

    expect(reading).toMatchObject({ output: { title: '', summary: '  ', text: 'a\tb\r\nc\nd\re ' } })
    expect(toCheckRunOutputObservation(reported({ name: ' padded name ' }), scope)).toMatchObject({ checkRun: { name: ' padded name ' } })
    expect(toCheckRunOutputObservation(reported({}, { title: null, summary: '', text: null }), scope)).toMatchObject({
      output: { title: null, summary: '', text: null },
    })
    expect(toCheckRunOutputObservation(reported({}, { text: '<script>x</script> [l](https://e.example) \\ "q" ' + ch(0x1f600) }), scope)).toMatchObject({
      output: { text: '<script>x</script> [l](https://e.example) \\ "q" ' + ch(0x1f600) },
    })
  })

  it('refuses a disallowed control, format, separator, replacement or lone surrogate in any text value', () => {
    for (const codePoint of [0x0000, 0x0001, 0x0008, 0x000b, 0x000c, 0x001b, 0x007f, 0x0085, 0x009f, 0x00ad, 0x200b, 0x200e, 0x202e, 0x2066, 0xfeff, 0x2028, 0x2029, 0xfffd]) {
      const bad = 'a' + ch(codePoint) + 'b'
      for (const field of ['title', 'summary', 'text']) {
        expect(toCheckRunOutputObservation(reported({}, { [field]: bad }), scope), `U+${codePoint.toString(16)} in ${field}`).toBeNull()
      }

      expect(toCheckRunOutputObservation(reported({ name: bad }), scope), `U+${codePoint.toString(16)} in name`).toBeNull()
    }

    for (const unit of [0xd800, 0xdbff, 0xdc00, 0xdfff]) {
      expect(toCheckRunOutputObservation(reported({}, { text: 'a' + lone(unit) + 'b' }), scope)).toBeNull()
      expect(toCheckRunOutputObservation(reported({}, { title: lone(unit) }), scope)).toBeNull()
    }

    expect(toCheckRunOutputObservation(reported({}, { text: 'ok ' + ch(0x1f600) + ch(0xe9) + ch(0x65e5) }), scope)).not.toBeNull()
  })

  it('refuses a name with a tab or line break even though the same characters are allowed in the output', () => {
    for (const name of ['a\tb', 'a\nb', 'a\rb', '', '  ']) {
      expect(toCheckRunOutputObservation(reported({ name }), scope)).toBeNull()
    }

    expect(toCheckRunOutputObservation(reported({}, { text: 'a\tb\nc\rd' }), scope)).not.toBeNull()
  })

  it('bounds each text value and the three together by UTF-8 bytes and refuses a non-string value', () => {
    expect(MAXIMUM_TITLE_BYTES).toBe(256)
    expect(MAXIMUM_SUMMARY_BYTES).toBe(8192)
    expect(MAXIMUM_TEXT_BYTES).toBe(16384)
    expect(MAXIMUM_OUTPUT_BYTES).toBe(24576)
    expect(toCheckRunOutputObservation(reported({}, { title: 't'.repeat(256) }), scope)).not.toBeNull()
    expect(toCheckRunOutputObservation(reported({}, { title: 't'.repeat(257) }), scope)).toBeNull()
    expect(toCheckRunOutputObservation(reported({}, { summary: 's'.repeat(8192) }), scope)).not.toBeNull()
    expect(toCheckRunOutputObservation(reported({}, { summary: 's'.repeat(8193) }), scope)).toBeNull()
    expect(toCheckRunOutputObservation(reported({}, { text: 'x'.repeat(16384) }), scope)).not.toBeNull()
    expect(toCheckRunOutputObservation(reported({}, { text: 'x'.repeat(16385) }), scope)).toBeNull()
    expect(toCheckRunOutputObservation(reported({}, { title: ch(0xe9).repeat(128) }), scope)).not.toBeNull()
    expect(toCheckRunOutputObservation(reported({}, { title: ch(0xe9).repeat(129) }), scope)).toBeNull()
    expect(toCheckRunOutputObservation(reported({}, { title: ch(0x1f600).repeat(64) }), scope)).not.toBeNull()
    expect(toCheckRunOutputObservation(reported({}, { title: ch(0x1f600).repeat(65) }), scope)).toBeNull()
    expect(toCheckRunOutputObservation(reported({ name: 'n'.repeat(256) }), scope)).not.toBeNull()
    expect(toCheckRunOutputObservation(reported({ name: 'n'.repeat(257) }), scope)).toBeNull()

    // The three maxima exceed the combined bound; 256 + 8192 + 16128 is exactly the bound.
    expect(toCheckRunOutputObservation(reported({}, { title: 't'.repeat(256), summary: 's'.repeat(8192), text: 'x'.repeat(16128) }), scope)).not.toBeNull()
    expect(toCheckRunOutputObservation(reported({}, { title: 't'.repeat(256), summary: 's'.repeat(8192), text: 'x'.repeat(16129) }), scope)).toBeNull()
    for (const value of [1, true, {}, [], ['a']]) {
      for (const field of ['title', 'summary', 'text']) {
        expect(toCheckRunOutputObservation(reported({}, { [field]: value }), scope)).toBeNull()
      }
    }
  })

  it('copies only the named members and nothing else the wire object carries', () => {
    const noisy = reported({ extra: 'x', url: 'https://evil.example' }, { extra: 'y' })
    const reading = toCheckRunOutputObservation(
      { ...noisy, observation: { ...(noisy.observation as object), extra: 'z' } } as unknown as GetGitHubCheckRunOutputObservationResponse,
      scope,
    )

    expect(reading).toEqual({
      status: 'OutputReported',
      binding: BINDING,
      facts: FACTS,
      ref: REF,
      checkRun: CHECK_RUN,
      output: { title: 'Title', summary: 'Summary', text: 'Text' },
    })
  })

  it('derives a lifetime key from every identifying fact of the source, the target and the selected check run', () => {
    const base = checkRunOutputScopeKey(scope)
    expect(checkRunOutputScopeKey({ ...scope })).toBe(base)
    for (const changed of [
      { ...scope, checkRunId: 'CR_other' },
      { ...scope, checkRunId: CHECK.toLowerCase() },
      { ...scope, source: { ...source, runId: 'other-run' } },
      { ...scope, source: { ...source, operationId: 'other-op' } },
      { ...scope, source: { ...source, commitSha: OTHER_SHA } },
      { ...scope, source: { ...source, branchName: 'other' } },
      { ...scope, target: { ...target, id: 'other' } },
      { ...scope, target: { ...target, eventId: 'other' } },
      { ...scope, target: { ...target, owner: 'other' } },
      { ...scope, target: { ...target, repository: 'other' } },
      { ...scope, target: { ...target, recordedAtUtc: new Date(RECORDED.getTime() + 1) } },
    ]) {
      expect(checkRunOutputScopeKey(changed)).not.toBe(base)
    }
  })

  it('has only fixed plain-text statements that never translate an outcome and always say the output is untrusted', () => {
    const statements = Object.values(CHECK_RUN_OUTPUT_STATEMENTS)
    for (const statement of statements) {
      expect(statement.length).toBeGreaterThan(10)
      expect(statement).not.toMatch(/https?:|<|>/)
      expect(statement).not.toMatch(/\b(passed|approved|ready|green)\b/i)
    }

    // The disclosure alone may name a diagnosis, and only to say that this is not one.
    for (const [name, statement] of Object.entries(CHECK_RUN_OUTPUT_STATEMENTS)) {
      if (name !== 'Disclosure') {
        expect(statement).not.toMatch(/\b(diagnos\w*|fix\w*|repair\w*|rerun)\b/i)
      }
    }

    expect(CHECK_RUN_OUTPUT_STATEMENTS.Disclosure).toMatch(/not an instruction, a diagnosis/i)
    expect(CHECK_RUN_OUTPUT_STATEMENTS.Disclosure).toMatch(/untrusted/i)
    expect(CHECK_RUN_OUTPUT_STATEMENTS.Disclosure).toMatch(/not (an )?instruction|not instructions/i)
    expect(CHECK_RUN_OUTPUT_STATEMENTS.Redaction).toMatch(/redact/i)
    expect(CHECK_RUN_OUTPUT_STATEMENTS.Redaction).toMatch(/five|supported/i)
    expect(CHECK_RUN_OUTPUT_STATEMENTS.NullField).toMatch(/no value/i)
    expect(CHECK_RUN_OUTPUT_STATEMENTS.EmptyField).toMatch(/empty/i)
    expect(CHECK_RUN_OUTPUT_STATEMENTS.WhitespaceField).toMatch(/whitespace/i)
  })
})
