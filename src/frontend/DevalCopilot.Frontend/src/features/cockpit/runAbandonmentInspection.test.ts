import { describe, expect, it } from 'vitest'
import { isCanonicalAbandonmentReason, isCanonicalEventSequence, toRunAbandonmentInspection } from './runAbandonmentInspection'
import {
  ABANDONMENT_EVENT,
  ABANDONMENT_OTHER_PROJECT,
  ABANDONMENT_OTHER_RUN,
  ABANDONMENT_PROJECT,
  ABANDONMENT_REASON,
  ABANDONMENT_RUN,
  ABANDONMENT_TARGET,
  notRecordedRunAbandonmentInspectionFor,
  recordedRunAbandonmentInspectionFor,
  runAbandonmentRecordedFor,
  unavailableRunAbandonmentInspectionFor,
} from './runAbandonmentInspectionFixture'

const decode = (raw: unknown) => toRunAbandonmentInspection(raw, ABANDONMENT_TARGET)
const withRecorded = (patch: Record<string, unknown>) => recordedRunAbandonmentInspectionFor({}, patch)
const point = (value: number) => String.fromCodePoint(value)

describe('toRunAbandonmentInspection (ADR-0071 whole-answer decoder)', () => {
  it('accepts exactly the three closed statuses and an abandonment only for Recorded', () => {
    expect(decode(notRecordedRunAbandonmentInspectionFor())).toEqual({ status: 'NotRecorded', runId: ABANDONMENT_RUN })
    expect(decode(unavailableRunAbandonmentInspectionFor())).toEqual({ status: 'Unavailable', runId: ABANDONMENT_RUN })
    expect(decode(notRecordedRunAbandonmentInspectionFor({ status: 'Pending' }))).toBeNull()
    expect(decode(notRecordedRunAbandonmentInspectionFor({ status: 'recorded' }))).toBeNull()
    expect(decode(notRecordedRunAbandonmentInspectionFor({ abandonment: runAbandonmentRecordedFor() }))).toBeNull()
    expect(decode(unavailableRunAbandonmentInspectionFor({ abandonment: runAbandonmentRecordedFor() }))).toBeNull()
    expect(decode(recordedRunAbandonmentInspectionFor({ abandonment: null }))).toBeNull()
    expect(decode(recordedRunAbandonmentInspectionFor({ abandonment: undefined }))).toBeNull()
  })

  it('accepts a Recorded answer as exactly its recorded facts', () => {
    expect(decode(recordedRunAbandonmentInspectionFor())).toEqual({
      status: 'Recorded',
      runId: ABANDONMENT_RUN,
      abandonment: {
        projectId: ABANDONMENT_PROJECT,
        reason: ABANDONMENT_REASON,
        abandonedAtUtc: '2026-10-08T12:00:00.1234567Z',
        eventId: ABANDONMENT_EVENT,
        eventSequence: '42',
      },
    })
  })

  it('refuses anything that is not a record with exactly the documented members at the root and inside the abandonment', () => {
    for (const raw of [undefined, null, 'x', 7, [], [recordedRunAbandonmentInspectionFor()]]) {
      expect(decode(raw)).toBeNull()
    }

    expect(decode({ ...recordedRunAbandonmentInspectionFor(), extra: 1 })).toBeNull()
    expect(decode({ ...notRecordedRunAbandonmentInspectionFor(), extra: null })).toBeNull()
    const { status: _status, ...noStatus } = notRecordedRunAbandonmentInspectionFor()
    expect(decode(noStatus)).toBeNull()
    const { abandonment: _abandonment, ...noAbandonment } = notRecordedRunAbandonmentInspectionFor()
    expect(decode(noAbandonment)).toBeNull()
    const { runId: _runId, ...noRun } = notRecordedRunAbandonmentInspectionFor()
    expect(decode(noRun)).toBeNull()

    for (const key of Object.keys(runAbandonmentRecordedFor())) {
      const { [key]: _removed, ...partial } = runAbandonmentRecordedFor()
      expect(decode(recordedRunAbandonmentInspectionFor({ abandonment: partial })), `missing ${key}`).toBeNull()
      expect(decode(withRecorded({ [key]: undefined })), `undefined ${key}`).toBeNull()
      expect(decode(withRecorded({ [key]: null })), `null ${key}`).toBeNull()
    }

    expect(decode(withRecorded({ extra: 'x' }))).toBeNull()
  })

  describe('identity', () => {
    it('requires exactly the requested run and the selected entry project', () => {
      expect(decode(notRecordedRunAbandonmentInspectionFor({ runId: ABANDONMENT_OTHER_RUN }))).toBeNull()
      expect(decode(recordedRunAbandonmentInspectionFor({ runId: ABANDONMENT_OTHER_RUN }))).toBeNull()
      expect(decode(withRecorded({ projectId: ABANDONMENT_OTHER_PROJECT }))).toBeNull()
      expect(toRunAbandonmentInspection(recordedRunAbandonmentInspectionFor(), { runId: ABANDONMENT_RUN, projectId: ABANDONMENT_OTHER_PROJECT })).toBeNull()
      expect(toRunAbandonmentInspection(notRecordedRunAbandonmentInspectionFor(), { runId: ABANDONMENT_OTHER_RUN, projectId: ABANDONMENT_PROJECT })).toBeNull()
    })

    it.each([
      ['uppercase', ABANDONMENT_EVENT.toUpperCase()],
      ['braced', `{${ABANDONMENT_EVENT}}`],
      ['unhyphenated', ABANDONMENT_EVENT.replaceAll('-', '')],
      ['padded', ` ${ABANDONMENT_EVENT}`],
      ['nil', '00000000-0000-0000-0000-000000000000'],
      ['empty', ''],
      ['number', 7],
      ['non-hex', 'cccccccc-0000-4000-8000-00000000000g'],
    ])('refuses a %s event identity', (_name, value) => {
      expect(decode(withRecorded({ eventId: value }))).toBeNull()
    })

    it('refuses a non-canonical project identity even when it names the selected entry', () => {
      const upper = ABANDONMENT_PROJECT.toUpperCase()

      expect(toRunAbandonmentInspection(withRecorded({ projectId: upper }), { runId: ABANDONMENT_RUN, projectId: upper })).toBeNull()
    })

    it('refuses an upper-case run identity even when requested', () => {
      const upper = ABANDONMENT_RUN.toUpperCase()

      expect(toRunAbandonmentInspection(notRecordedRunAbandonmentInspectionFor({ runId: upper }), { ...ABANDONMENT_TARGET, runId: upper })).toBeNull()
    })
  })

  describe('the canonical reason (ADR-0031)', () => {
    it('keeps LF line breaks, inner blanks and any scalar that is not control, format or a separator, literally', () => {
      const multiline = 'First line\n\nThird  line with  spaces\n  indented'
      const unicode = ['Unicode ', point(0xe9), ' ', point(0x4e2d), ' ', point(0x1f600), ' end'].join('')

      for (const reason of [multiline, unicode, 'x', '<b>not markup</b> & "quotes"']) {
        const answer = decode(withRecorded({ reason }))
        expect(answer?.status === 'Recorded' ? answer.abandonment.reason : null).toBe(reason)
      }
    })

    it.each([
      ['empty', ''],
      ['blank', '   '],
      ['only a line feed', '\n'],
      ['leading blank', ' reason'],
      ['trailing blank', 'reason '],
      ['leading line feed', '\nreason'],
      ['trailing line feed', 'reason\n'],
      ['leading no-break space', `${point(0xa0)}reason`],
      ['trailing ideographic space', `reason${point(0x3000)}`],
      ['carriage return', 'a\rb'],
      ['CRLF', 'a\r\nb'],
      ['NUL', `a${point(0)}b`],
      ['tab', 'a\tb'],
      ['C0 control', `a${point(7)}b`],
      ['DEL', `a${point(0x7f)}b`],
      ['C1 control', `a${point(0x85)}b`],
      ['line separator', `a${point(0x2028)}b`],
      ['paragraph separator', `a${point(0x2029)}b`],
      ['zero-width space (format)', `a${point(0x200b)}b`],
      ['byte-order mark (format)', `a${point(0xfeff)}b`],
      ['bidi override (format)', `a${point(0x202e)}b`],
      ['supplementary-plane format character', `a${point(0xe0001)}b`],
      ['unpaired high surrogate', `a${String.fromCharCode(0xd800)}b`],
      ['unpaired low surrogate', `a${String.fromCharCode(0xdc00)}b`],
      ['a number', 7],
      ['a list', ['a']],
    ])('refuses %s', (_name, reason) => {
      expect(decode(withRecorded({ reason }))).toBeNull()
    })

    it('bounds the reason at exactly 2048 UTF-8 bytes and not by characters', () => {
      const ascii = 'a'.repeat(2048)
      const twoByte = point(0xe9).repeat(1024)
      const threeByte = `${point(0x4e2d).repeat(682)}ab`
      const fourByte = point(0x1f600).repeat(512)

      for (const reason of [ascii, twoByte, threeByte, fourByte]) {
        expect(new TextEncoder().encode(reason).length).toBe(2048)
        expect(decode(withRecorded({ reason }))?.status, reason.slice(0, 4)).toBe('Recorded')
      }

      for (const reason of [`${ascii}a`, `${twoByte}a`, `${point(0xe9).repeat(1025)}`, `${point(0x4e2d).repeat(683)}`, `${fourByte}a`]) {
        expect(new TextEncoder().encode(reason).length).toBeGreaterThan(2048)
        expect(decode(withRecorded({ reason })), reason.slice(0, 4)).toBeNull()
      }
    })

    it('exposes the same decision as a predicate', () => {
      expect(isCanonicalAbandonmentReason(ABANDONMENT_REASON)).toBe(true)
      expect(isCanonicalAbandonmentReason(' x')).toBe(false)
      expect(isCanonicalAbandonmentReason(undefined)).toBe(false)
    })
  })

  describe('the recorded instant', () => {
    it.each([
      '2026-10-08T12:00:00.0000000Z',
      '2026-10-08T12:00:00.1234567Z',
      '2026-10-08T12:00:00.0000001Z',
      '2026-10-08T23:59:59.9999999Z',
      '0001-01-01T00:00:00.0000000Z',
      '0001-01-01T00:00:00.0000001Z',
      '9999-12-31T23:59:59.9999999Z',
    ])('admits the canonical seven-digit UTC instant %s, the default instant included', (abandonedAtUtc) => {
      const answer = decode(withRecorded({ abandonedAtUtc }))

      expect(answer?.status === 'Recorded' ? answer.abandonment.abandonedAtUtc : null).toBe(abandonedAtUtc)
    })

    it.each([
      ['six fractional digits', '2026-10-08T12:00:00.123456Z'],
      ['no fraction', '2026-10-08T12:00:00Z'],
      ['milliseconds only', '2026-10-08T12:00:00.123Z'],
      ['eight fractional digits', '2026-10-08T12:00:00.12345678Z'],
      ['a positive offset', '2026-10-08T15:00:00.0000000+03:00'],
      ['a zero offset', '2026-10-08T12:00:00.0000000+00:00'],
      ['a lower-case z', '2026-10-08T12:00:00.0000000z'],
      ['a space separator', '2026-10-08 12:00:00.0000000Z'],
      ['a lower-case t', '2026-10-08t12:00:00.0000000Z'],
      ['an impossible month', '2026-13-08T12:00:00.0000000Z'],
      ['an impossible day', '2026-02-30T12:00:00.0000000Z'],
      ['an impossible hour', '2026-10-08T24:00:00.0000000Z'],
      ['trailing text', '2026-10-08T12:00:00.0000000Z '],
      ['empty', ''],
      ['a number', 1_790_000_000_000],
      ['a Date', new Date(0)],
    ])('refuses %s', (_name, abandonedAtUtc) => {
      expect(decode(withRecorded({ abandonedAtUtc }))).toBeNull()
    })
  })

  describe('the event sequence', () => {
    it.each(['1', '2', '9', '10', '42', '9007199254740993', '9223372036854775806', '9223372036854775807'])(
      'admits the canonical positive decimal Int64 text %s exactly',
      (eventSequence) => {
        const answer = decode(withRecorded({ eventSequence }))

        expect(answer?.status === 'Recorded' ? answer.abandonment.eventSequence : null).toBe(eventSequence)
        expect(isCanonicalEventSequence(eventSequence)).toBe(true)
      },
    )

    it.each([
      ['zero', '0'],
      ['negative', '-1'],
      ['a plus sign', '+1'],
      ['a leading zero', '01'],
      ['a leading zero run', '000'],
      ['one beyond Int64', '9223372036854775808'],
      ['a long overflow', '92233720368547758070'],
      ['a decimal', '1.5'],
      ['a whole decimal', '1.0'],
      ['an exponent', '1e3'],
      ['a hexadecimal', '0x10'],
      ['padded', ' 1'],
      ['trailing blank', '1 '],
      ['a separator', '1,000'],
      ['an underscore', '1_000'],
      ['an Arabic-Indic digit', point(0x661)],
      ['empty', ''],
      ['a number', 42],
      ['a safe number', Number.MAX_SAFE_INTEGER],
      ['a bigint', 42n],
      ['null', null],
    ])('refuses %s', (_name, eventSequence) => {
      expect(decode(withRecorded({ eventSequence }))).toBeNull()
      expect(isCanonicalEventSequence(eventSequence)).toBe(false)
    })
  })

  it('never partially accepts: one violation anywhere rejects the whole answer', () => {
    const base = withRecorded({})
    expect(decode(base)?.status).toBe('Recorded')

    for (const patch of [{ reason: ' x' }, { eventSequence: '0' }, { abandonedAtUtc: 'x' }, { eventId: 'x' }, { projectId: ABANDONMENT_OTHER_PROJECT }]) {
      expect(decode(withRecorded(patch))).toBeNull()
    }
  })
})
