namespace DevalCopilot.Api.Features.Runs.GetAgentSupervision;

/// <summary>The selected Agent attempt exactly as recorded, with its real status and outcome. <c>Dispatched</c> says only that the
/// single-use dispatch marker exists (<c>DispatchedAtUtc</c> is then present); it is not proof that a process runs. Times are UTC
/// round-trip strings.</summary>
public sealed record AgentSupervisionAttemptResponse(
    Guid AttemptId,
    int AttemptNumber,
    string Role,
    string Provider,
    string ResponseContract,
    string Status,
    string? Outcome,
    bool Dispatched,
    string ClaimedAtUtc,
    string? DispatchedAtUtc,
    string? CompletedAtUtc);
