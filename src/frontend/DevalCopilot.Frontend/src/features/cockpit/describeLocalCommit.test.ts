import { describe, expect, it } from 'vitest'
import { ApiException } from '../../api/generated/api-client'
import {
  LOCAL_COMMIT_MAX_MESSAGE_BYTES,
  classifyLocalCommitFailure,
  describeLocalCommitOutcomeReason,
  describeLocalCommitRefusal,
  validateLocalCommitMessage,
} from './describeLocalCommit'

const problem = (status: number, code?: string) => new ApiException('raw', status, JSON.stringify({ errors: [{ code }] }), {}, null)

describe('validateLocalCommitMessage', () => {
  it('accepts a trimmed multi-line message and returns it trimmed', () => {
    expect(validateLocalCommitMessage('  Subject\n\nBody line  \n')).toEqual({ valid: true, message: 'Subject\n\nBody line' })
  })

  it.each([
    ['', 'empty'],
    ['   \n  ', 'empty'],
    ['Subject\r\nBody', 'carriage_return'],
    ['Sub\u0000ject', 'control_character'],
    ['Sub\tject', 'control_character'],
    ['Sub\u007fject', 'control_character'],
  ])('rejects %j as %s', (draft, problemName) => {
    expect(validateLocalCommitMessage(draft)).toEqual({ valid: false, problem: problemName })
  })

  it('measures the limit in UTF-8 bytes, not characters', () => {
    expect(validateLocalCommitMessage('a'.repeat(LOCAL_COMMIT_MAX_MESSAGE_BYTES)).valid).toBe(true)
    expect(validateLocalCommitMessage('a'.repeat(LOCAL_COMMIT_MAX_MESSAGE_BYTES + 1))).toEqual({ valid: false, problem: 'too_long' })
    expect(validateLocalCommitMessage('é'.repeat(LOCAL_COMMIT_MAX_MESSAGE_BYTES / 2 + 1))).toEqual({ valid: false, problem: 'too_long' })
  })
})

describe('describeLocalCommitRefusal', () => {
  it('maps known codes with or without the local_commit prefix and never echoes unknown ones', () => {
    expect(describeLocalCommitRefusal('local_commit.human_decision_not_approved')).toMatch(/new checkpoint/)
    expect(describeLocalCommitRefusal('local_commit.operation_exists')).toMatch(/already has a recorded/)
    expect(describeLocalCommitRefusal('refused.conversion_refused')).toMatch(/line-ending/)
    expect(describeLocalCommitRefusal('local_commit.refused.too_many_paths')).toMatch(/more paths/)
    expect(describeLocalCommitRefusal('local_commit.refused.hooks_directory_not_empty')).toMatch(/cannot be committed by the host as configured/)
    expect(describeLocalCommitRefusal('local_commit.totally_new')).not.toContain('totally_new')
    expect(describeLocalCommitRefusal(undefined)).toMatch(/not available/)
  })
})

describe('describeLocalCommitOutcomeReason', () => {
  it('is null without a code, fixed for known codes and generic for unknown ones', () => {
    expect(describeLocalCommitOutcomeReason(undefined)).toBeNull()
    expect(describeLocalCommitOutcomeReason('local_commit.git_unprovable')).toMatch(/could not be proven/)
    expect(describeLocalCommitOutcomeReason('local_commit.something_else')).not.toContain('something_else')
  })
})

describe('classifyLocalCommitFailure', () => {
  it('treats a non-HTTP failure and a 5xx as unknown outcomes', () => {
    expect(classifyLocalCommitFailure(new TypeError('Failed to fetch'))).toEqual({ kind: 'unknown' })
    expect(classifyLocalCommitFailure(problem(500))).toEqual({ kind: 'unknown' })
    expect(classifyLocalCommitFailure(problem(503, 'local_commit.operation_conflict'))).toEqual({ kind: 'unknown' })
  })

  it('treats other HTTP responses as definite refusals with fixed copy', () => {
    expect(classifyLocalCommitFailure(problem(409, 'local_commit.checkpoint_not_current'))).toMatchObject({ kind: 'refused' })
    expect(classifyLocalCommitFailure(problem(409))).toMatchObject({ kind: 'refused', message: expect.stringContaining('Reload the run') })
    expect(classifyLocalCommitFailure(problem(404))).toMatchObject({ kind: 'refused', message: 'This run could not be found.' })
    expect(classifyLocalCommitFailure(new ApiException('raw', 409, 'not json', {}, null))).toMatchObject({ kind: 'refused' })
  })
})
