namespace DevalCopilot.Application.Features.Projects.Queries.GetEligibleVerificationExecutions;

public sealed record EligibleVerificationExecution(
    Guid VerificationExecutionId,
    string CheckpointFingerprintSha256,
    string ExecutablePath,
    IReadOnlyList<string> Arguments,
    string WorkspacePath,
    int TimeoutSeconds);
