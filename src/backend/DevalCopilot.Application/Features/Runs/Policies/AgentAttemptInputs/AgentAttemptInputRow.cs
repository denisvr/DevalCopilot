namespace DevalCopilot.Application.Features.Runs.Policies.AgentAttemptInputs;

/// <summary>One stored input row of the attempt. <see cref="Position"/> is null unless the database proved the stored position an
/// integer from 0 through 63, so malformed storage is never converted into a valid position.</summary>
internal sealed record AgentAttemptInputRow(int? Position, Guid CollaborationMessageId);
