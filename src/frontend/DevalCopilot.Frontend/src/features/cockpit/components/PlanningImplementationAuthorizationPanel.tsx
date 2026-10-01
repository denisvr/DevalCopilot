import type { PlanningImplementationAuthorizationResponse } from '../../../api/clients'
import { PlanningAuthorizationForm } from './PlanningAuthorizationForm'

interface PlanningImplementationAuthorizationPanelProps {
  runId: string
  /** The planning escalation the loaded timeline shows answering the lineage's final revision. */
  escalationMessageId: string
  /** The final revision that same timeline shows; the server's own facts must name the same Proposal. */
  timelineFinalProposalMessageId: string
  /** The server's current facts for this exact (run, escalation), or `null` while unread or after a failed read. */
  authorization: PlanningImplementationAuthorizationResponse | null
  loading: boolean
  readError: string | null
  authorizing: boolean
  authorizeError: string | null
  onAuthorize: (rationale: string) => Promise<boolean>
}

const shortId = (id: string) => id.slice(0, 8)

/**
 * The explicit human decision on the final plan of a completed second challenge round. It states the plan and
 * second-round decisions the server will use, and the exact consequence of authorizing: one implementation claim, spent
 * by the claim itself even if the attempt later fails, with no provider contact, budget reservation, or approval implied.
 * What it shows is only what the server read says (absent, available, consumed, stale, or invalid) together with the
 * exact recorded reason; a successful click is never read as a state, and when the facts cannot be read no decision is
 * shown. Requesting the implementation is a separate action elsewhere and provider/budget checks stay separate.
 */
export function PlanningImplementationAuthorizationPanel({
  runId,
  escalationMessageId,
  timelineFinalProposalMessageId,
  authorization,
  loading,
  readError,
  authorizing,
  authorizeError,
  onAuthorize,
}: PlanningImplementationAuthorizationPanelProps) {
  const finalProposalMessageId = authorization?.finalProposalMessageId ?? null
  const mismatched = finalProposalMessageId !== null && finalProposalMessageId !== timelineFinalProposalMessageId
  const decisions = authorization?.orderedDecisionMessageIds ?? []

  return (
    <section className="dc-planning-authorization" aria-label="Human decision on the final plan">
      <h3 className="dc-planning-authorization-heading">Human decision on the final plan</h3>
      <p className="dc-planning-authorization-identity">
        Final revised plan {shortId(timelineFinalProposalMessageId)} of escalation {shortId(escalationMessageId)}
        {decisions.length > 0 ? ` · ${decisions.length} second-round decision${decisions.length === 1 ? '' : 's'} (${decisions.map(shortId).join(', ')})` : ''}.
      </p>

      {readError && (
        <p className="dc-planning-authorization-unavailable" role="status">
          {readError}
        </p>
      )}
      {!readError && !authorization && loading && (
        <p className="dc-planning-authorization-loading" aria-busy="true">
          Reading the authorization status…
        </p>
      )}
      {authorization && mismatched && (
        <p className="dc-planning-authorization-mismatch" role="status">
          The server names a different final plan than the loaded timeline shows, so no decision is offered here.
        </p>
      )}

      {authorization && !mismatched && authorization.state === 'Absent' && (
        <>
          <p className="dc-planning-authorization-consequence">
            Authorizing records your decision that this final revised plan may be implemented, and permits exactly one
            implementation claim of it. It does not start an implementation, reserve any budget, or contact a provider;
            you request the implementation separately, and provider availability and budgets are checked then. The
            claim spends the authorization even if the implementation later fails, and it cannot be renewed or revoked.
          </p>
          <PlanningAuthorizationForm
            runId={runId}
            escalationMessageId={escalationMessageId}
            finalProposalMessageId={timelineFinalProposalMessageId}
            authorizing={authorizing}
            statusLoading={loading}
            onSubmit={onAuthorize}
          />
        </>
      )}
      {authorization && !mismatched && authorization.state === 'Available' && (
        <>
          <p className="dc-planning-authorization-state" role="status">
            Authorized by a human for exactly one implementation claim of this plan. No implementation has been
            claimed with it yet. Provider availability and budgets are checked separately when you request it.
          </p>
          {authorization.rationale && (
            <p className="dc-planning-authorization-rationale">Recorded reason: {authorization.rationale}</p>
          )}
        </>
      )}
      {authorization && !mismatched && authorization.state === 'Consumed' && (
        <>
          <p className="dc-planning-authorization-state" role="status">
            The authorization was used by an implementation claim and cannot be reused, whatever that attempt&apos;s
            outcome.
          </p>
          {authorization.rationale && (
            <p className="dc-planning-authorization-rationale">Recorded reason: {authorization.rationale}</p>
          )}
        </>
      )}
      {authorization && !mismatched && authorization.state === 'Stale' && (
        <p className="dc-planning-authorization-state" role="status">
          This escalation or its authorization no longer matches the run&apos;s current plan, checkpoint, or fingerprint,
          so it cannot be used. Nothing is implemented through it.
        </p>
      )}
      {authorization && !mismatched && authorization.state === 'Invalid' && (
        <p className="dc-planning-authorization-state" role="status">
          The recorded evidence for this escalation or authorization could not be validated, so it is not usable and no
          decision is offered.
        </p>
      )}
      {authorization &&
        !mismatched &&
        !['Absent', 'Available', 'Consumed', 'Stale', 'Invalid'].includes(authorization.state ?? '') && (
          <p className="dc-planning-authorization-state" role="status">
            The authorization state is not recognized, so no decision is offered.
          </p>
        )}

      {authorizeError && (
        <p className="dc-planning-authorization-error" role="status">
          {authorizeError}
        </p>
      )}
    </section>
  )
}
