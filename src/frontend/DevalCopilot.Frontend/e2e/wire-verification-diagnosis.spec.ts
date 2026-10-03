import { randomUUID } from 'node:crypto'
import { expect, test } from '@playwright/test'
import {
  ApiException,
  GetAgentAttemptEvidenceEndpointClient,
  GetRunCockpitEndpointClient,
  GetVerificationDiagnosisStatusEndpointClient,
  RequestDiagnosisCorrectionEndpointClient,
  RequestDiagnosisCorrectionRequest,
  RequestVerificationDiagnosisEndpointClient,
  RequestVerificationDiagnosisRequest,
} from '../src/api/generated/api-client'
import { describeDirectGuidance } from '../src/features/cockpit/describeDirectGuidance'
import { describeDirectGuidanceFailure } from '../src/features/cockpit/hooks/directGuidanceFailure'
import { API_BASE_URL } from '../playwright.config'
import { authorizedHttp } from './planningAuthorizationFixture'
import { createVerificationDiagnosisFixture, insertDiagnosisCorrectionAttempt, setAttemptGuidance } from './verificationDiagnosisFixture'

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

const SENTINEL = 'SENTINEL-WIRE-DIAG-41'
const ACCEPTED = `Reuse the existing helper.\nKeep it small. ${SENTINEL}`

test('the generated client serializes diagnosis-correction guidance and revives the recorded fact over real HTTP', async () => {
  test.setTimeout(90_000)
  const fixture = await createVerificationDiagnosisFixture('verification-diagnosis-guidance-wire')
  const status = new GetVerificationDiagnosisStatusEndpointClient(API_BASE_URL, authorizedHttp)
  const correct = new RequestDiagnosisCorrectionEndpointClient(API_BASE_URL, authorizedHttp)
  const evidenceClient = new GetAgentAttemptEvidenceEndpointClient(API_BASE_URL, authorizedHttp)
  const cockpitClient = new GetRunCockpitEndpointClient(API_BASE_URL, authorizedHttp)
  const send = (guidance?: string) =>
    correct.requestDiagnosisCorrection(
      fixture.runId,
      new RequestDiagnosisCorrectionRequest({ verificationDiagnosisAttemptId: randomUUID(), guidance }),
    )

  // Request serialization: the guidance member reaches the server and is validated before anything else, and is never echoed.
  for (const guidance of [`${SENTINEL} the password is x`, '   ', '', 'x'.repeat(601), 'bell\u0007']) {
    const refused = await rejectionOf(send(guidance))
    expect(refused.status).toBe(400)
    const message = describeDirectGuidanceFailure(refused)
    expect(message).toContain('The guidance was not accepted.')
    expect(JSON.stringify([refused.message, refused.response, message])).not.toContain(SENTINEL)
  }

  // Valid guidance and omitted guidance pass validation identically: an unknown diagnosis is the same domain refusal.
  const withGuidance = await rejectionOf(send(ACCEPTED))
  const withoutGuidance = await rejectionOf(send(undefined))
  expect(withGuidance.status).toBe(withoutGuidance.status)
  expect([404, 409]).toContain(withGuidance.status)
  expect(describeDirectGuidanceFailure(withGuidance)).toBeNull()
  expect(JSON.stringify(withGuidance.response)).not.toContain(SENTINEL)

  // No correction: no fact.
  const before = await status.getVerificationDiagnosisStatus(fixture.runId)
  expect(before.attemptId).toBe(fixture.diagnosisAttemptId)
  expect(before.correctionDirectGuidance ?? null).toBeNull()

  // Revival of the fact in the diagnosis status, the attempt evidence and the cockpit: Provided, Unknown (malformed, no text), NotRecorded.
  const correctionId = insertDiagnosisCorrectionAttempt(fixture, ACCEPTED)
  const read = async () => ({
    status: (await status.getVerificationDiagnosisStatus(fixture.runId)).correctionDirectGuidance,
    evidence: (await evidenceClient.getAgentAttemptEvidence(fixture.runId, correctionId)).directGuidance,
    cockpit: (await cockpitClient.getRunCockpit(fixture.runId)).latestAgentAttempt?.directGuidance,
  })
  const named = await status.getVerificationDiagnosisStatus(fixture.runId)
  expect(named.correctionAttemptId).toBe(correctionId)

  const provided = await read()
  for (const fact of [provided.status, provided.evidence, provided.cockpit]) {
    expect(fact?.state).toBe('Provided')
    expect(fact?.text).toBe(ACCEPTED)
    expect(describeDirectGuidance(fact)).toEqual({ kind: 'provided', text: ACCEPTED })
  }

  setAttemptGuidance(correctionId, ` ${SENTINEL} padded and unnormalized `)
  const unknown = await read()
  for (const fact of [unknown.status, unknown.evidence, unknown.cockpit]) {
    expect(fact?.state).toBe('Unknown')
    expect(fact?.text ?? null).toBeNull()
    expect(describeDirectGuidance(fact)).toEqual({ kind: 'unknown' })
  }

  setAttemptGuidance(correctionId, null)
  const recorded = await read()
  for (const fact of [recorded.status, recorded.evidence, recorded.cockpit]) {
    expect(fact?.state).toBe('NotRecorded')
    expect(fact?.text ?? null).toBeNull()
    expect(describeDirectGuidance(fact)).toEqual({ kind: 'notRecorded' })
  }
})
