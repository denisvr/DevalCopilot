using DevalCopilot.Application.Data;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Policies.AgentAttemptInputs;

/// <summary>
/// Proves the whole recorded collaboration-input set of one Agent attempt from bounded scalar reads (ADR-0051), for both the reference
/// inspection and every selected input-message read (ADR-0053), which deliberately change together. It applies the unchanged scalar
/// identity evaluation (ADR-0037), reads at most 65 of the attempt's ordered input rows through the attempt/sequence index, judges at
/// most 64 and resolves every referenced message identity inside the same run before any input is returned. It never selects a
/// message summary or content JSON, follows no reply, repair or input chain, writes nothing and swallows no fault.
/// </summary>
internal static class RecordedAgentAttemptInputSetReader
{
    /// <summary>The most input rows this inspection judges. It bounds this read only: it is not a limit on claims, stored inputs or the
    /// message-linked reader, and a larger set is reported as unavailable, never as invalid.</summary>
    public const int MaximumInspectedInputs = AgentAttemptInputsProjection.PositionBound;

    public static async Task<RecordedAgentAttemptInputSetReading> ReadAsync(
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
            return RecordedAgentAttemptInputSetReading.NotFound;
        }

        var identity = ScalarAgentAttemptIdentity.Evaluate(attempt);
        if (identity is null)
        {
            return RecordedAgentAttemptInputSetReading.Unavailable;
        }

        // One more row than can be inspected, in stored order through the (AttemptId, Sequence) index: observing it is enough to refuse.
        var inputs = await dbContext.AttemptInputMessages.AsNoTracking()
            .Where(input => input.AttemptId == attemptId)
            .OrderBy(input => input.Sequence)
            .Take(MaximumInspectedInputs + 1)
            .ProjectInput()
            .ToArrayAsync(cancellationToken);
        if (inputs.Length > MaximumInspectedInputs)
        {
            return RecordedAgentAttemptInputSetReading.Unavailable;
        }

        // Only a Planner starts from no prior collaboration fact; every other role records at least one input, so zero rows for it may
        // be a lost set and is never shown as an innocent empty one.
        if (inputs.Length == 0)
        {
            return identity.Role == AgentRole.Planner
                ? RecordedAgentAttemptInputSetReading.Proven(new RecordedAgentAttemptInputSet(attempt.Id, attempt.AttemptNumber, identity, []))
                : RecordedAgentAttemptInputSetReading.Unavailable;
        }

        // The whole set is judged before anything is returned: gapless positions from zero and unique, nonempty references.
        var referenced = new HashSet<Guid>();
        for (var index = 0; index < inputs.Length; index++)
        {
            if (inputs[index].Position != index
                || inputs[index].CollaborationMessageId == Guid.Empty
                || !referenced.Add(inputs[index].CollaborationMessageId))
            {
                return RecordedAgentAttemptInputSetReading.Unavailable;
            }
        }

        // At most the referenced identities, inside this run only; message identities are unique, so this is at most 64 rows. A missing
        // or foreign reference is never omitted or replaced.
        var referencedIds = referenced.ToArray();
        var messages = await dbContext.CollaborationMessages.AsNoTracking()
            .Where(message => message.RunId == runId && referencedIds.Contains(message.Id))
            .ProjectReferencedMessage()
            .ToArrayAsync(cancellationToken);
        var messagesById = new Dictionary<Guid, AgentAttemptReferencedMessageRow>();
        foreach (var message in messages)
        {
            if (!messagesById.TryAdd(message.Id, message))
            {
                return RecordedAgentAttemptInputSetReading.Unavailable;
            }
        }

        var recorded = new RecordedAgentAttemptInput[inputs.Length];
        for (var index = 0; index < inputs.Length; index++)
        {
            if (!messagesById.TryGetValue(inputs[index].CollaborationMessageId, out var message)
                || !message.VersionKnown
                || message.TypeCode is < 1 or > 11
                || message.Sequence <= 0)
            {
                return RecordedAgentAttemptInputSetReading.Unavailable;
            }

            recorded[index] = new RecordedAgentAttemptInput(
                index,
                message.Id,
                AgentAttemptInputsProjection.TypesByCode[message.TypeCode - 1],
                message.Sequence,
                message.OccurredAtUtc);
        }

        return RecordedAgentAttemptInputSetReading.Proven(
            new RecordedAgentAttemptInputSet(attempt.Id, attempt.AttemptNumber, identity, recorded));
    }
}
