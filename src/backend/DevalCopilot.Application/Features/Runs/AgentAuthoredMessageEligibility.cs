using DevalCopilot.Application.Data;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs;

/// <summary>
/// The shared read-side eligibility check for a real, provider-observed Agent-authored
/// collaboration message consumed as a later attempt's workflow input: role-first, never
/// provider-first, per ADR-0009. <see cref="AgentProvider"/> participates only as
/// provenance-integrity evidence — the message's own persisted <c>Actor</c> must still truthfully
/// match its owning attempt's real provider — never as a fixed value compared to authorize a role.
/// Operation-specific rules (the message's own expected <c>Type</c>, outcome, checkpoint,
/// cardinality, uniqueness, and reply chain) remain each caller's own responsibility; this helper
/// owns only ownership/provenance/role/contract coherence, mirroring the exact eligibility facts
/// every <c>Create*Attempt</c> handler independently re-derived before this correction.
/// </summary>
internal static class AgentAuthoredMessageEligibility
{
    /// <summary>
    /// Resolves and validates the real Agent attempt that authored <paramref name="message"/>, or
    /// <see langword="null"/> if any of: the message does not belong to <paramref name="runId"/>,
    /// is not <see cref="CollaborationMessageProvenance.ProviderObserved"/>, has no owning attempt,
    /// that attempt does not belong to the same run, is not <see cref="AttemptKind.Agent"/>, does
    /// not have <paramref name="expectedRole"/>, its response contract is not coherent with that
    /// role's <see cref="AgentAttemptContract"/>, its provider is absent or not a defined
    /// <see cref="AgentProvider"/> value, or its provider no longer truthfully matches the
    /// message's own recorded <c>Actor</c>. Malformed persisted state (a missing or undefined
    /// provider) is treated exactly like any other ineligibility — this method fails closed by
    /// returning <see langword="null"/>, never by throwing. The message's own <c>Type</c>, outcome,
    /// checkpoint, and any cardinality/uniqueness rule remain the caller's own responsibility to
    /// check.
    /// </summary>
    public static async Task<Attempt?> ResolveOwningAttemptAsync(
        IDevalCopilotDbContext dbContext,
        CollaborationMessage message,
        Guid runId,
        AgentRole expectedRole,
        CancellationToken cancellationToken)
    {
        if (message.RunId != runId
            || message.Provenance != CollaborationMessageProvenance.ProviderObserved
            || message.AttemptId is not { } attemptId)
        {
            return null;
        }

        var attempt = await dbContext.Attempts.SingleOrDefaultAsync(candidate => candidate.Id == attemptId, cancellationToken);
        if (attempt is null
            || attempt.RunId != runId
            || attempt.Kind != AttemptKind.Agent
            || attempt.AgentRole != expectedRole
            || attempt.AgentResponseContract is not { } responseContract
            || AgentAttemptContract.For(responseContract).Role != expectedRole
            || attempt.AgentProvider is not { } provider
            || !Enum.IsDefined(provider)
            || message.Actor != ParticipantIdentity.ForAgent(expectedRole, provider))
        {
            return null;
        }

        return attempt;
    }
}
