using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Runs.Queries.GetCollaborationHistory;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Policies.AgentAttemptOutputs;

/// <summary>
/// Proves the whole recorded output set of one Agent attempt from bounded scalar reads (ADR-0054), for both the reference inspection and
/// every selected output-message read, which deliberately change together. It applies the unchanged scalar identity evaluation
/// (ADR-0037), selects at most 65 of the messages whose own attempt reference is this attempt, in sequence order through the existing
/// attempt-reference index, judges at most 64 and refuses every malformed or foreign association before any reference is returned. The
/// association is selected before the run is checked, so a foreign-run message is refused, never hidden. It selects no text, reply
/// pointer or unrelated attempt or message, reconstructs no expected output from outcome, events or replies, writes nothing and swallows
/// no fault.
/// </summary>
internal static class RecordedAgentAttemptOutputSetReader
{
    /// <summary>The most associated messages this inspection judges. It bounds this read only: it is not a limit on stored outputs or on
    /// what a result handler records, and a larger set is reported as unavailable, never as invalid.</summary>
    public const int MaximumInspectedOutputs = 64;

    public static async Task<RecordedAgentAttemptOutputSetReading> ReadAsync(
        IDevalCopilotDbContext dbContext, Guid runId, Guid attemptId, CancellationToken cancellationToken)
    {
        // Exactly this attempt of this run, as a bounded scalar projection; an unknown run, a missing or foreign attempt and a non-Agent
        // attempt are the same answer.
        var attempt = await dbContext.Attempts.AsNoTracking()
            .Where(candidate => candidate.RunId == runId && candidate.Id == attemptId)
            .ProjectScalarIdentity()
            .FirstOrDefaultAsync(cancellationToken);
        if (attempt is null || !attempt.IsAgent)
        {
            return RecordedAgentAttemptOutputSetReading.NotFound;
        }

        var identity = ScalarAgentAttemptIdentity.Evaluate(attempt);
        if (identity is null)
        {
            return RecordedAgentAttemptOutputSetReading.Unavailable;
        }

        // One more row than can be inspected, in sequence order through the attempt-reference index: observing it is enough to refuse.
        var rows = await dbContext.CollaborationMessages.AsNoTracking()
            .Where(message => message.AttemptId == attemptId)
            .OrderBy(message => message.Sequence)
            .Take(MaximumInspectedOutputs + 1)
            .ProjectOutput()
            .ToArrayAsync(cancellationToken);
        if (rows.Length > MaximumInspectedOutputs)
        {
            return RecordedAgentAttemptOutputSetReading.Unavailable;
        }

        // Zero associated rows is a valid empty set for any role, dispatched or not: it states only what this read found.
        if (rows.Length == 0)
        {
            return RecordedAgentAttemptOutputSetReading.Proven(
                new RecordedAgentAttemptOutputSet(attempt.Id, attempt.AttemptNumber, identity, []));
        }

        // A message cannot have been produced by an attempt that was never dispatched.
        if (attempt.DispatchedAtUtc is null)
        {
            return RecordedAgentAttemptOutputSetReading.Unavailable;
        }

        // The whole set is judged before anything is returned. Nothing is filtered, substituted or repaired.
        var allowed = CollaborationMessageAuthorPolicy.AllowedMessageTypes(identity.Role);
        var identifiers = new HashSet<Guid>();
        var outputs = new RecordedAgentAttemptOutput[rows.Length];
        var previousSequence = 0L;
        for (var index = 0; index < rows.Length; index++)
        {
            var row = rows[index];
            var type = CollaborationHistoryRows.TypeOf(row.TypeCode);
            if (row.RunId != runId
                || row.Id == Guid.Empty
                || !identifiers.Add(row.Id)
                || row.Sequence <= previousSequence
                || !row.VersionKnown
                || type is not { } knownType
                || !allowed.Contains(knownType)
                || !IsProviderObservedAuthor(row.ProvenanceCode, row.ActorKindCode, row.ActorRoleCode, row.ActorProviderCode, identity))
            {
                return RecordedAgentAttemptOutputSetReading.Unavailable;
            }

            previousSequence = row.Sequence;
            outputs[index] = new RecordedAgentAttemptOutput(index, row.Id, knownType, row.Sequence, row.OccurredAtUtc);
        }

        return RecordedAgentAttemptOutputSetReading.Proven(
            new RecordedAgentAttemptOutputSet(attempt.Id, attempt.AttemptNumber, identity, outputs));
    }

    /// <summary>Whether database-classified provenance and actor codes state a provider-observed message whose Agent actor is exactly the
    /// attempt's own role and provider. Used for the whole-set proof and again for the reread of one selected member.</summary>
    public static bool IsProviderObservedAuthor(
        int provenanceCode, int actorKindCode, int actorRoleCode, int actorProviderCode, CoherentAgentAttemptIdentity identity) =>
        provenanceCode == AgentAttemptOutputsProjection.ProviderObservedCode
        && actorKindCode == AgentAttemptOutputsProjection.AgentKindCode
        && actorRoleCode == RoleCode(identity.Role)
        && actorProviderCode == ProviderCode(identity.Provider);

    private static int RoleCode(AgentRole role) => role switch
    {
        AgentRole.Planner => 1,
        AgentRole.CriticalReviewer => 2,
        AgentRole.Resolver => 3,
        AgentRole.Implementer => 4,
        AgentRole.CodeReviewer => 5,
        _ => -1,
    };

    private static int ProviderCode(AgentProvider provider) => provider switch
    {
        AgentProvider.Codex => 1,
        AgentProvider.ClaudeCode => 2,
        _ => -1,
    };
}
