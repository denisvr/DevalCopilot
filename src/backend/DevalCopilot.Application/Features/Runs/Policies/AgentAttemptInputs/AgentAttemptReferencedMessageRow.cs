namespace DevalCopilot.Application.Features.Runs.Policies.AgentAttemptInputs;

/// <summary>The stored metadata of one referenced message of the run, classified by the database without materializing a stored enum:
/// <see cref="TypeCode"/> is 0 for a type not known. No text column is part of it.</summary>
internal sealed record AgentAttemptReferencedMessageRow(Guid Id, long Sequence, bool VersionKnown, int TypeCode, DateTimeOffset OccurredAtUtc);
