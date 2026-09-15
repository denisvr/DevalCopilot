namespace DevalCopilot.Api.Features.Projects.ConfigureVerificationCommand;

public sealed record ConfigureVerificationCommandRequest(
    string Name,
    string ExecutablePath,
    IReadOnlyCollection<string> Arguments,
    int TimeoutSeconds,
    bool IsEnabled);
