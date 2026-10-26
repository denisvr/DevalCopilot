namespace DevalCopilot.Application.Features.Projects.Policies.ProjectVerificationInspection;

/// <summary>One admitted history member: the exact execution identity, its positive number, its snapshot command name and its claim
/// time as read now.</summary>
internal sealed record ProjectVerificationHistoryEntry(Guid ExecutionId, int ExecutionNumber, string CommandName, DateTimeOffset ClaimedAtUtc);
