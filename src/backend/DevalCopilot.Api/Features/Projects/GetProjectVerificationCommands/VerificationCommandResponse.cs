namespace DevalCopilot.Api.Features.Projects.GetProjectVerificationCommands;

public sealed record VerificationCommandResponse(
    Guid VerificationCommandId,
    int CommandNumber,
    string Name,
    string ExecutablePath,
    IReadOnlyList<string> Arguments,
    int TimeoutSeconds,
    bool IsEnabled,
    DateTimeOffset UpdatedAtUtc);
