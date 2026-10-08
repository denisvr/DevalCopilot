import { describe, expect, it, vi } from 'vitest'
import {
  ApiException,
  GetCheckpointApprovalEvidenceEndpointClient,
  RecordCheckpointReviewEndpointClient,
  RecordCheckpointReviewRequest,
} from './generated/api-client'

// The protected approval-evidence read and the execution-set form of the existing review POST go through the generated client
// only. The read resolves the typed bundle (identity, ordered members), keeps its refusal's fixed code, and the set form
// serializes the identifiers as one array, omitting the legacy scalar.
function answering(response: Response) {
  const fetch = vi.fn(() => Promise.resolve(response))
  return { fetch, bundleClient: new GetCheckpointApprovalEvidenceEndpointClient('http://host.invalid', { fetch }) }
}

describe('GetCheckpointApprovalEvidenceEndpointClient', () => {
  it('reads the bundle with one GET and resolves its typed identity and members', async () => {
    const body = JSON.stringify({
      projectId: 'project-1',
      workspaceId: 'workspace-1',
      checkpointId: 'checkpoint-1',
      checkpointNumber: 3,
      fingerprintSha256: 'f'.repeat(64),
      members: [
        { verificationCommandId: 'command-1', commandNumber: 1, recipeLabel: 'Unit', verificationExecutionId: 'execution-1', executionNumber: 4 },
        { verificationCommandId: 'command-2', commandNumber: 2, recipeLabel: 'Lint', verificationExecutionId: 'execution-2', executionNumber: 5 },
      ],
    })
    const { bundleClient, fetch } = answering(new Response(body, { status: 200, headers: { 'Content-Type': 'application/json' } }))

    const bundle = await bundleClient.getCheckpointApprovalEvidence('project-1', 'checkpoint-1')

    expect(fetch).toHaveBeenCalledTimes(1)
    const [url, init] = fetch.mock.calls[0] as unknown as [string, RequestInit]
    expect(url).toBe('http://host.invalid/api/projects/project-1/checkpoints/checkpoint-1/approval-evidence')
    expect(init.method).toBe('GET')
    expect(bundle.checkpointId).toBe('checkpoint-1')
    expect(bundle.members?.map(member => [member.commandNumber, member.recipeLabel, member.verificationExecutionId, member.executionNumber])).toEqual([
      [1, 'Unit', 'execution-1', 4],
      [2, 'Lint', 'execution-2', 5],
    ])
  })

  it('rejects a refusal as an API exception that keeps its fixed code, never as a bundle', async () => {
    const problem = JSON.stringify({ errors: [{ code: 'approval_evidence.verification_incomplete', detail: 'fixed' }] })
    const { bundleClient } = answering(new Response(problem, { status: 409, headers: { 'Content-Type': 'application/json' } }))

    const refusal = await bundleClient.getCheckpointApprovalEvidence('project-1', 'checkpoint-1').then(
      () => null,
      (caught: unknown) => caught,
    )

    expect(ApiException.isApiException(refusal)).toBe(true)
    expect((refusal as ApiException).status).toBe(409)
    expect((refusal as ApiException).response).toContain('approval_evidence.verification_incomplete')
  })

  it('encodes route identifiers instead of splicing them into the path', async () => {
    const { bundleClient, fetch } = answering(new Response('{}', { status: 200, headers: { 'Content-Type': 'application/json' } }))

    await bundleClient.getCheckpointApprovalEvidence('a/b', 'c?d')

    expect((fetch.mock.calls[0] as unknown as [string])[0]).toBe('http://host.invalid/api/projects/a%2Fb/checkpoints/c%3Fd/approval-evidence')
  })
})

describe('RecordCheckpointReviewRequest execution-set form', () => {
  it('serializes the identifiers as one array and omits the legacy scalar', async () => {
    const fetch = vi.fn(() => Promise.resolve(new Response(JSON.stringify({ reviewId: 'r', decision: 'Approved' }), { status: 201, headers: { 'Content-Type': 'application/json' } })))
    const client = new RecordCheckpointReviewEndpointClient('http://host.invalid', { fetch })

    await client.recordCheckpointReview('project-1', new RecordCheckpointReviewRequest({
      gitCheckpointId: 'checkpoint-1',
      actorKind: 'Human',
      decision: 'Approved',
      verificationExecutionIds: ['execution-1', 'execution-2'],
    }))

    const [, init] = fetch.mock.calls[0] as unknown as [string, RequestInit]
    const sent = JSON.parse(init.body as string) as Record<string, unknown>
    expect(sent).toEqual({
      gitCheckpointId: 'checkpoint-1',
      actorKind: 'Human',
      decision: 'Approved',
      verificationExecutionIds: ['execution-1', 'execution-2'],
    })
    expect('verificationExecutionId' in sent && sent.verificationExecutionId !== undefined).toBe(false)
  })

  it('keeps the legacy scalar form byte-compatible when no set is supplied', async () => {
    const fetch = vi.fn(() => Promise.resolve(new Response(JSON.stringify({ reviewId: 'r', decision: 'Approved' }), { status: 201, headers: { 'Content-Type': 'application/json' } })))
    const client = new RecordCheckpointReviewEndpointClient('http://host.invalid', { fetch })

    await client.recordCheckpointReview('project-1', new RecordCheckpointReviewRequest({
      gitCheckpointId: 'checkpoint-1',
      verificationExecutionId: 'execution-1',
      actorKind: 'Human',
      decision: 'Approved',
    }))

    const [, init] = fetch.mock.calls[0] as unknown as [string, RequestInit]
    const sent = JSON.parse(init.body as string) as Record<string, unknown>
    expect(sent).toEqual({ gitCheckpointId: 'checkpoint-1', verificationExecutionId: 'execution-1', actorKind: 'Human', decision: 'Approved' })
  })
})
