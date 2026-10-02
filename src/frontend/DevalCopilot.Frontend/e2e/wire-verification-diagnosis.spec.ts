import { randomUUID } from 'node:crypto'
import { expect, test } from '@playwright/test'
import {
  ApiException,
  GetVerificationDiagnosisStatusEndpointClient,
  RequestDiagnosisCorrectionEndpointClient,
  RequestDiagnosisCorrectionRequest,
  RequestVerificationDiagnosisEndpointClient,
  RequestVerificationDiagnosisRequest,
} from '../src/api/generated/api-client'
import { API_BASE_URL } from '../playwright.config'
import { authorizedHttp } from './planningAuthorizationFixture'
import { createVerificationDiagnosisFixture } from './verificationDiagnosisFixture'

// The REAL generated TypeScript client over real HTTP against the actual host and a database owned by this run: request
// serialization, response revival, and refusal mapping of the verification-failure diagnosis operations (ADR-0018). The
// evidence chain is the owned raw-SQL fixture (see verificationDiagnosisFixture.ts), not production-written evidence; no
// provider is ever started and no attempt is claimed.

async function rejectionOf(promise: Promise<unknown>): Promise<ApiException> {
  try {
    await promise
  } catch (caught) {
    if (ApiException.isApiException(caught)) {
      return caught
    }
    throw new Error('Expected an ApiException from the generated client.')
  }
  throw new Error('Expected the request to be refused.')
}

test('the generated client revives the diagnosis status and maps unclaimable requests to safe refusals', async () => {
  // Registration retries on the host's Git readiness refusal only; budget 45 x 1 s fits this timeout.
  test.setTimeout(90_000)
  const fixture = await createVerificationDiagnosisFixture('verification-diagnosis-wire')
  const status = new GetVerificationDiagnosisStatusEndpointClient(API_BASE_URL, authorizedHttp)
  const diagnose = new RequestVerificationDiagnosisEndpointClient(API_BASE_URL, authorizedHttp)
  const correct = new RequestDiagnosisCorrectionEndpointClient(API_BASE_URL, authorizedHttp)

  const read = await status.getVerificationDiagnosisStatus(fixture.runId)
  expect(read.hasAttempt).toBe(true)
  expect(read.attemptId).toBe(fixture.diagnosisAttemptId)
  expect(read.executionReportMessageId).toBe(fixture.executionReportId)
  expect(read.outcome).toBe('DiagnosisFindingsRecorded')
  expect(read.findingCount).toBe(2)
  expect(read.verification?.map((member) => [member.position, member.commandName, member.status, member.exitCode])).toEqual([
    [1, 'unit', 'Failed', 1],
    [2, 'lint', 'Passed', 0],
  ])
  expect(JSON.stringify(read)).not.toContain('fixture-tool')

  // An unknown run and an unknown report are safe refusals that echo nothing.
  const unknownRun = await rejectionOf(status.getVerificationDiagnosisStatus(randomUUID()))
  expect(unknownRun.status).toBe(404)
  const unknownReport = await rejectionOf(
    diagnose.requestVerificationDiagnosis(
      fixture.runId,
      new RequestVerificationDiagnosisRequest({ executionReportMessageId: randomUUID() }),
    ),
  )
  expect([404, 409]).toContain(unknownReport.status)
  expect(JSON.stringify(unknownReport.response)).not.toContain('fixture-tool')

  // A correction naming an unknown diagnosis claims nothing.
  const unknownDiagnosis = await rejectionOf(
    correct.requestDiagnosisCorrection(
      fixture.runId,
      new RequestDiagnosisCorrectionRequest({ verificationDiagnosisAttemptId: randomUUID() }),
    ),
  )
  expect([404, 409]).toContain(unknownDiagnosis.status)

  // Nothing above recorded an attempt: the status still names the one fixture diagnosis.
  const after = await status.getVerificationDiagnosisStatus(fixture.runId)
  expect(after.attemptId).toBe(fixture.diagnosisAttemptId)
})
