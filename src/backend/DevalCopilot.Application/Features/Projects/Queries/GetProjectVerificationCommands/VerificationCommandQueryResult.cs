namespace DevalCopilot.Application.Features.Projects.Queries.GetProjectVerificationCommands;

public sealed record VerificationCommandQueryResult(
    Guid VerificationCommandId,
    int CommandNumber,
    string Name,
    string ExecutablePath,
    IReadOnlyList<string> Arguments,
    int TimeoutSeconds,
    bool IsEnabled,
    DateTimeOffset UpdatedAtUtc);
