import { expect, test } from '@playwright/test'
import {
  ApiException,
  AuthorizePlanningImplementationEndpointClient,
  AuthorizePlanningImplementationRequest,
  GetPlanningImplementationAuthorizationEndpointClient,
} from '../src/api/generated/api-client'
import { describePlanningAuthorizationFailure } from '../src/features/cockpit/planningAuthorizationFailure'
import { API_BASE_URL } from '../playwright.config'
import { authorizedHttp, createPlanningAuthorizationFixture } from './planningAuthorizationFixture'
import { randomUUID } from 'node:crypto'

// The REAL generated TypeScript client over real HTTP against the actual host and a database owned by this run, with the
// launch secret as an Authorization header: request serialization, response revival, and error mapping of the human
// implementation authorization (ADR-0016). The planning lineage is an owned metadata fixture (see
// planningAuthorizationFixture.ts); no provider is ever started and no implementation is requested. It registers its own project
// in the shared run-owned database, so no execution order is assumed.

const SENTINEL = 'SENTINEL-WIRE-AUTH-9'
const REASON = `I reviewed both rounds and accept the final plan. ${SENTINEL}`

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

test('the generated client authorizes the final plan once, revives its facts, and maps every refusal to fixed copy', async () => {
  // Registration retries on the host's Git readiness refusal only; budget 45 x 1 s fits this timeout.
  test.setTimeout(90_000)
  const fixture = await createPlanningAuthorizationFixture('planning-authorization-wire')
  const authorize = new AuthorizePlanningImplementationEndpointClient(API_BASE_URL, authorizedHttp)
  const read = new GetPlanningImplementationAuthorizationEndpointClient(API_BASE_URL, authorizedHttp)
  const send = (rationale: string) =>
    authorize.authorizePlanningImplementation(fixture.runId, fixture.escalationId, new AuthorizePlanningImplementationRequest({ rationale }))

  // Before any decision: a valid current source, nothing recorded, and the ordered decision identities of the final round.
  const absent = await read.getPlanningImplementationAuthorization(fixture.runId, fixture.escalationId)
  expect(absent.state).toBe('Absent')
  expect(absent.finalProposalMessageId).toBe(fixture.finalProposalId)
  expect(absent.orderedDecisionMessageIds).toEqual(fixture.decisionIds)
  expect(absent.authorizationId ?? null).toBeNull()
  expect(absent.rationale ?? null).toBeNull()

  // Validation is refused whole, with fixed copy and never an echo of the text.
  for (const bad of ['', '   ', 'x'.repeat(601), `${SENTINEL} the password is x`]) {
    const refused = await rejectionOf(send(bad))
    expect(refused.status).toBe(400)
    const message = describePlanningAuthorizationFailure(refused)
    expect(message).toContain('The reason was not accepted.')
    expect(JSON.stringify([refused.message, refused.response, message])).not.toContain(SENTINEL)
  }

  // An unknown escalation and the final Proposal (not an escalation) are not found, for both operations.
  for (const messageId of [randomUUID(), fixture.finalProposalId]) {
    const missing = await rejectionOf(
      authorize.authorizePlanningImplementation(fixture.runId, messageId, new AuthorizePlanningImplementationRequest({ rationale: REASON })),
    )
    const missingRead = await rejectionOf(read.getPlanningImplementationAuthorization(fixture.runId, messageId))
    expect(missing.status).toBe(404)
    expect(missingRead.status).toBe(404)
    expect(JSON.stringify([missing.response, missingRead.response])).not.toContain(SENTINEL)
  }

  // Nothing above recorded anything.
  expect((await read.getPlanningImplementationAuthorization(fixture.runId, fixture.escalationId)).state).toBe('Absent')

  // The decision: a bounded, identity-only response that never echoes the reason.
  const first = await send(REASON)
  expect(first.status).toBe('Authorized')
  expect(first.escalationMessageId).toBe(fixture.escalationId)
  expect(first.finalProposalMessageId).toBe(fixture.finalProposalId)
  expect(first.authorizationId).toBeTruthy()
  expect(first.humanInstructionMessageId).toBeTruthy()
  expect(JSON.stringify(first)).not.toContain(SENTINEL)

  const available = await read.getPlanningImplementationAuthorization(fixture.runId, fixture.escalationId)
  expect(available.state).toBe('Available')
  expect(available.authorizationId).toBe(first.authorizationId)
  expect(available.humanInstructionMessageId).toBe(first.humanInstructionMessageId)
  expect(available.rationale).toBe(REASON)
  expect(available.finalProposalMessageId).toBe(fixture.finalProposalId)
  expect(available.consumedByAttemptId ?? null).toBeNull()

  // An identical retry (even re-padded) is the same authorization; a different reason is a safe conflict.
  const retry = await send(`  ${REASON}  `)
  expect(retry.authorizationId).toBe(first.authorizationId)
  expect(retry.humanInstructionMessageId).toBe(first.humanInstructionMessageId)
  const conflict = await rejectionOf(send('A different reason entirely.'))
  expect(conflict.status).toBe(409)
  expect(describePlanningAuthorizationFailure(conflict)).toContain('different reason already exists')
  expect(JSON.stringify(conflict.response)).not.toContain('different reason entirely')

  // Still exactly one authorization, unchanged.
  const after = await read.getPlanningImplementationAuthorization(fixture.runId, fixture.escalationId)
  expect(after.state).toBe('Available')
  expect(after.authorizationId).toBe(first.authorizationId)
})
