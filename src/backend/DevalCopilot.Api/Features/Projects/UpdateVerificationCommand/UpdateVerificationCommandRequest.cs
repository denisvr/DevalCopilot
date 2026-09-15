namespace DevalCopilot.Api.Features.Projects.UpdateVerificationCommand;

public sealed record UpdateVerificationCommandRequest(
    string Name,
    string ExecutablePath,
    IReadOnlyCollection<string> Arguments,
    int TimeoutSeconds,
    bool IsEnabled);
