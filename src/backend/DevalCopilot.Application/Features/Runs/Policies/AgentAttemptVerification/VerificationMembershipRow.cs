namespace DevalCopilot.Application.Features.Runs.Policies.AgentAttemptVerification;

/// <summary>One stored membership row of the attempt. <see cref="Position"/> is null unless the database proved the stored position an
/// integer inside the inspected range, so malformed storage is never converted into a valid position. No digest is part of it.</summary>
internal sealed record VerificationMembershipRow(int? Position, Guid CommandId, Guid ExecutionId);
