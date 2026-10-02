import { describe, expect, it } from 'vitest'
import { ApiException } from '../../api/generated/api-client'
import { describeVerificationDiagnosisFailure } from './verificationDiagnosisFailure'

const GENERIC = 'generic fallback'

function problem(code: string, detail = 'server detail'): ApiException {
  return new ApiException('failed', 409, JSON.stringify({ errors: [{ code, detail }] }), {}, null)
}

describe('describeVerificationDiagnosisFailure', () => {
  it('describes an unavailable provider runtime, distinct from a report without provider provenance', () => {
    const runtime = describeVerificationDiagnosisFailure(problem('agent_attempts.provider_not_observed'), GENERIC)
    const provenance = describeVerificationDiagnosisFailure(
      problem('agent_attempts.not_provider_observed_execution_report'),
      GENERIC,
    )

    expect(runtime).toContain('provider runtime is not currently observed as available')
    expect(runtime).not.toContain('implementation was not observed')
    expect(provenance).toContain('not a provider-observed implementation report')
    expect(runtime).not.toBe(provenance)
  })

  it.each([
    ['agent_attempts.token_stop_reached', 'token stop has been reached'],
    ['agent_attempts.token_stop_evidence_indeterminate', 'could not be determined'],
    ['agent_attempts.token_stop_policy_changed', 'policy changed'],
  ])('uses fixed copy for the real token-stop code %s without echoing server text', (code, expected) => {
    const message = describeVerificationDiagnosisFailure(problem(code, 'SENTINEL-DETAIL'), GENERIC)

    expect(message).toContain(expected)
    expect(message).not.toContain('SENTINEL-DETAIL')
  })

  it('keeps the truthful persistence-ambiguity wording', () => {
    expect(describeVerificationDiagnosisFailure(problem('attempts.persistence_unresolved'), GENERIC)).toContain(
      'could not confirm whether the request was recorded',
    )
  })

  it('falls back to the generic sentence for malformed responses and raw exceptions', () => {
    expect(describeVerificationDiagnosisFailure(new ApiException('x', 500, 'not json', {}, null), GENERIC)).toBe(GENERIC)
    expect(describeVerificationDiagnosisFailure(new ApiException('x', 500, '{"errors":[]}', {}, null), GENERIC)).toBe(GENERIC)
    expect(describeVerificationDiagnosisFailure(new Error('boom at C:\\secret'), GENERIC)).toBe(GENERIC)
  })
})
