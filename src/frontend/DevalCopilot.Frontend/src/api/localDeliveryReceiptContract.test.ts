import { describe, expect, it, vi } from 'vitest'
import { ApiException, GetLocalDeliveryReceiptEndpointClient } from './generated/api-client'

// The protected receipt read goes through the generated client only. It resolves the typed closed-state response (nested identity
// records, ordered members and revived UTC times), keeps the fixed code of a refusal, and never splices the run into the path.
function answering(response: Response) {
  const fetch = vi.fn(() => Promise.resolve(response))
  return { fetch, receiptClient: new GetLocalDeliveryReceiptEndpointClient('http://host.invalid', { fetch }) }
}

const json = (body: unknown, status = 200) =>
  new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })

describe('GetLocalDeliveryReceiptEndpointClient', () => {
  it('reads the receipt with one bodyless GET and resolves its typed identity, approvals and ordered members', async () => {
    const { receiptClient, fetch } = answering(
      json({
        state: 'Available',
        receipt: {
          version: 1,
          runId: 'run-1',
          operationId: 'operation-1',
          objective: 'Implement the ledger',
          commitSha: 'c'.repeat(40),
          parentCommitSha: 'p'.repeat(40),
          treeSha: 't'.repeat(40),
          branchName: 'devalcopilot/workspace/x/1',
          completedAtUtc: '2026-10-09T13:00:00+00:00',
          checkpoint: { id: 'checkpoint-1', number: 3, fingerprintSha256: 'f'.repeat(64), changedPathCount: 2 },
          executionReportMessageId: 'report-1',
          codeReview: { attemptId: 'attempt-1', attemptNumber: 5, approvalMessageId: 'approval-1' },
          humanReview: { reviewId: 'human-1', decision: 'Approved' },
          verification: [
            { order: 0, commandId: 'command-1', executionId: 'execution-1', executionNumber: 4, commandName: 'Unit', status: 'Passed', exitCode: 0, completedAtUtc: '2026-10-09T12:00:00+00:00' },
            { order: 1, commandId: 'command-2', executionId: 'execution-2', executionNumber: 5, commandName: 'Lint', status: 'Passed', exitCode: 0, completedAtUtc: '2026-10-09T12:01:00+00:00' },
          ],
        },
      }),
    )

    const response = await receiptClient.getLocalDeliveryReceipt('run-1')

    expect(fetch).toHaveBeenCalledTimes(1)
    const [url, init] = fetch.mock.calls[0] as unknown as [string, RequestInit]
    expect(url).toBe('http://host.invalid/api/runs/run-1/local-delivery-receipt')
    expect(init.method).toBe('GET')
    expect(init.body).toBeUndefined()
    expect(response.state).toBe('Available')
    expect(response.receipt?.checkpoint?.number).toBe(3)
    expect(response.receipt?.codeReview?.approvalMessageId).toBe('approval-1')
    expect(response.receipt?.humanReview?.decision).toBe('Approved')
    expect(response.receipt?.completedAtUtc).toBeInstanceOf(Date)
    expect(response.receipt?.verification?.map((member) => [member.order, member.commandName, member.executionNumber])).toEqual([
      [0, 'Unit', 4],
      [1, 'Lint', 5],
    ])
    expect(response.receipt?.verification?.[1].completedAtUtc).toBeInstanceOf(Date)
  })

  it('resolves each receipt-less state with a null receipt', async () => {
    for (const state of ['NotRecorded', 'NotCompleted', 'Unavailable']) {
      const { receiptClient } = answering(json({ state, receipt: null }))

      const response = await receiptClient.getLocalDeliveryReceipt('run-1')

      expect(response.state).toBe(state)
      expect(response.receipt).toBeUndefined()
    }
  })

  it('rejects a refusal as an API exception that keeps its fixed code, never as a receipt', async () => {
    const { receiptClient } = answering(json({ errors: [{ code: 'runs.not_found', detail: 'fixed' }] }, 404))

    const refusal = await receiptClient.getLocalDeliveryReceipt('run-1').then(
      () => null,
      (caught: unknown) => caught,
    )

    expect(ApiException.isApiException(refusal)).toBe(true)
    expect((refusal as ApiException).status).toBe(404)
    expect((refusal as ApiException).response).toContain('runs.not_found')
  })

  it('encodes the run identifier instead of splicing it into the path', async () => {
    const { receiptClient, fetch } = answering(json({ state: 'NotRecorded' }))

    await receiptClient.getLocalDeliveryReceipt('run/../x?y=1')

    const [url] = fetch.mock.calls[0] as unknown as [string]
    expect(url).toBe('http://host.invalid/api/runs/run%2F..%2Fx%3Fy%3D1/local-delivery-receipt')
  })
})
