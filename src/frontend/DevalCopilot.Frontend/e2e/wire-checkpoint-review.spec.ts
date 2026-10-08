import { randomUUID } from 'node:crypto'
import { expect, test } from '@playwright/test'
import {
  ApiException,
  GetCheckpointApprovalEvidenceEndpointClient,
  GetProjectCheckpointReviewsEndpointClient,
  RecordCheckpointReviewEndpointClient,
  RecordCheckpointReviewRequest,
} from '../src/api/generated/api-client'
import { API_BASE_URL } from '../playwright.config'
import { authorizedHttp } from './planningAuthorizationFixture'
import { createCheckpointReviewFixture } from './checkpointReviewFixture'

// The REAL generated TypeScript client over real HTTP against the actual host and a database owned by this run: the manual
// checkpoint review answers HTTP 201 (Created) and the generated client must resolve that answer with the typed review identity
// and decision, while a refusal keeps its Problem Details. The terminal verification executions are an owned raw-SQL fixture
// (see checkpointReviewFixture.ts); no provider and no verification process is ever started.

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

test('the generated client resolves the host\'s 201 review acceptance with its typed identity and keeps refusals as Problem Details', async () => {
  // Registration retries on the host's Git readiness refusal only; budget 45 x 1 s fits this timeout.
  test.setTimeout(90_000)
  const fixture = await createCheckpointReviewFixture('checkpoint-review-wire')
  const record = new RecordCheckpointReviewEndpointClient(API_BASE_URL, authorizedHttp)
  const history = new GetProjectCheckpointReviewsEndpointClient(API_BASE_URL, authorizedHttp)
  const send = (decision: string, executionId?: string, checkpointId = fixture.checkpointId) =>
    record.recordCheckpointReview(
      fixture.projectId,
      new RecordCheckpointReviewRequest({ gitCheckpointId: checkpointId, verificationExecutionId: executionId, actorKind: 'Human', decision }),
    )

  expect(await history.getProjectCheckpointReviews(fixture.projectId)).toEqual([])

  // Pending is execution-free and, through the generated client, an ordinary success rather than an unexpected status.
  const pending = await send('Pending')
  expect(pending.reviewId).toBeTruthy()
  expect(pending.decision).toBe('Pending')

  const approved = await send('Approved', fixture.passedExecutionId)
  expect(approved.reviewId).toBeTruthy()
  expect(approved.reviewId).not.toBe(pending.reviewId)
  expect(approved.decision).toBe('Approved')

  // Approval of a Failed execution, a missing execution, another checkpoint and an unknown project are refused with Problem Details.
  const failedApproval = await rejectionOf(send('Approved', fixture.failedExecutionId))
  expect(failedApproval.status).toBe(409)
  expect(failedApproval.response).toContain('reviews.approval_requires_passed_verification')
  const missingEvidence = await rejectionOf(send('ChangesRequested', randomUUID()))
  expect(missingEvidence.status).toBe(404)
  expect(missingEvidence.response).toContain('reviews.evidence_not_found')
  const notCurrent = await rejectionOf(send('Pending', undefined, randomUUID()))
  expect(notCurrent.status).toBe(409)
  expect(notCurrent.response).toContain('reviews.checkpoint_not_current')
  const unknownProject = await rejectionOf(
    record.recordCheckpointReview(randomUUID(), new RecordCheckpointReviewRequest({ gitCheckpointId: fixture.checkpointId, actorKind: 'Human', decision: 'Pending' })),
  )
  expect(unknownProject.status).toBe(404)
  for (const refusal of [failedApproval, missingEvidence, notCurrent, unknownProject]) {
    expect(refusal.response).not.toContain('fixture-tool')
    expect(refusal.response).not.toContain('System.')
  }

  // The failed approval may still be recorded as a changes-requested fact citing that execution.
  const changes = await send('ChangesRequested', fixture.failedExecutionId)
  expect(changes.decision).toBe('ChangesRequested')

  // Exactly the three accepted facts exist, each bound to the current checkpoint; the refusals recorded nothing.
  const reviews = await history.getProjectCheckpointReviews(fixture.projectId)
  expect(reviews.map((review) => review.reviewId).sort()).toEqual([pending.reviewId, approved.reviewId, changes.reviewId].sort())
  expect(reviews.every((review) => review.gitCheckpointId === fixture.checkpointId && review.isApplicable === true)).toBe(true)
  const approvedFact = reviews.find((review) => review.reviewId === approved.reviewId)
  expect(approvedFact?.actorKind).toBe('Human')
  expect(approvedFact?.evidence?.map((member) => [member.verificationExecutionId, member.verificationExecutionStatus])).toEqual([
    [fixture.passedExecutionId, 'Passed'],
  ])
  expect(reviews.find((review) => review.reviewId === pending.reviewId)?.evidence ?? []).toEqual([])

  // The complete approval bundle is not offered while an enabled recipe's latest execution has not Passed: a fixed refusal, no members.
  const incomplete = await rejectionOf(
    new GetCheckpointApprovalEvidenceEndpointClient(API_BASE_URL, authorizedHttp).getCheckpointApprovalEvidence(fixture.projectId, fixture.checkpointId),
  )
  expect(incomplete.status).toBe(409)
  expect(incomplete.response).toContain('approval_evidence.verification_incomplete')
  expect(incomplete.response).not.toContain('fixture-tool')
})

test('the generated client reads the complete approval bundle and records one complete-set approval over real HTTP, keeping refusals as Problem Details', async () => {
  test.setTimeout(90_000)
  const fixture = await createCheckpointReviewFixture('checkpoint-approval-wire', { lintStatus: 'Passed' })
  const bundles = new GetCheckpointApprovalEvidenceEndpointClient(API_BASE_URL, authorizedHttp)
  const record = new RecordCheckpointReviewEndpointClient(API_BASE_URL, authorizedHttp)
  const history = new GetProjectCheckpointReviewsEndpointClient(API_BASE_URL, authorizedHttp)
  const lower = (id: string | undefined) => id?.toLowerCase()
  // The fixture names its two recipes unit (command 1) and lint (command 2); both executions Passed for the current checkpoint.
  const [unitExecution, lintExecution] = [fixture.passedExecutionId, fixture.failedExecutionId]
  const send = (executionIds: string[], scalar?: string, decision = 'Approved') =>
    record.recordCheckpointReview(
      fixture.projectId,
      new RecordCheckpointReviewRequest({
        gitCheckpointId: fixture.checkpointId,
        verificationExecutionId: scalar,
        verificationExecutionIds: executionIds,
        actorKind: 'Human',
        decision,
      }),
    )

  const bundle = await bundles.getCheckpointApprovalEvidence(fixture.projectId, fixture.checkpointId)
  expect([bundle.projectId, bundle.checkpointId, bundle.fingerprintSha256].map(lower)).toEqual(
    [fixture.projectId, fixture.checkpointId, fixture.checkpointFingerprint].map(lower),
  )
  expect(bundle.members?.map((member) => [member.commandNumber, member.recipeLabel, lower(member.verificationCommandId), lower(member.verificationExecutionId), member.executionNumber])).toEqual([
    [1, 'unit', lower(fixture.unitCommandId), lower(unitExecution), 1],
    [2, 'lint', lower(fixture.lintCommandId), lower(lintExecution), 2],
  ])
  expect(await history.getProjectCheckpointReviews(fixture.projectId)).toEqual([]) // the read recorded nothing

  const unknownCheckpoint = await rejectionOf(bundles.getCheckpointApprovalEvidence(fixture.projectId, randomUUID()))
  expect(unknownCheckpoint.status).toBe(409)
  expect(unknownCheckpoint.response).toContain('reviews.checkpoint_not_current')
  const unknownProject = await rejectionOf(bundles.getCheckpointApprovalEvidence(randomUUID(), fixture.checkpointId))
  expect(unknownProject.status).toBe(404)

  // Two evidence forms, a subset, a duplicate and an empty decided set are refused as Problem Details and record nothing.
  const ambiguous = await rejectionOf(send([lintExecution], unitExecution))
  expect(ambiguous.status).toBe(422)
  expect(ambiguous.response).toContain('reviews.evidence_forms_ambiguous')
  const subset = await rejectionOf(send([unitExecution]))
  expect(subset.status).toBe(409)
  expect(subset.response).toContain('reviews.approval_requires_complete_verification_set')
  const duplicate = await rejectionOf(send([unitExecution, unitExecution]))
  expect(duplicate.status).toBe(422)
  expect(duplicate.response).toContain('reviews.evidence_selection_invalid')
  const empty = await rejectionOf(send([]))
  expect(empty.status).toBe(422)
  expect(empty.response).toContain('reviews.evidence_selection_invalid')
  const foreign = await rejectionOf(send([unitExecution, randomUUID()]))
  expect(foreign.status).toBe(404)
  expect(foreign.response).toContain('reviews.evidence_not_found')
  expect(await history.getProjectCheckpointReviews(fixture.projectId)).toEqual([])
  for (const refusal of [ambiguous, subset, duplicate, empty, foreign, unknownCheckpoint, unknownProject]) {
    expect(refusal.response).not.toContain('fixture-tool')
    expect(refusal.response).not.toContain('System.')
  }

  // The complete set, in any submitted order, is one accepted review with both members.
  const approved = await send([lintExecution, unitExecution])
  expect(approved.reviewId).toBeTruthy()
  expect(approved.decision).toBe('Approved')
  const reviews = await history.getProjectCheckpointReviews(fixture.projectId)
  expect(reviews).toHaveLength(1)
  expect(reviews[0].actorKind).toBe('Human')
  expect(reviews[0].isApplicable).toBe(true)
  expect(new Set(reviews[0].evidence?.map((member) => [lower(member.verificationCommandId), lower(member.verificationExecutionId), member.verificationExecutionStatus].join('|')))).toEqual(
    new Set([
      [lower(fixture.unitCommandId), lower(unitExecution), 'Passed'].join('|'),
      [lower(fixture.lintCommandId), lower(lintExecution), 'Passed'].join('|'),
    ]),
  )
})
