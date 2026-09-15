using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Projects.Commands.ConfigureVerificationCommand;

public sealed record ConfigureVerificationCommandCommand(
    Guid ProjectId,
    string Name,
    string ExecutablePath,
    IReadOnlyCollection<string> Arguments,
    int TimeoutSeconds,
    bool IsEnabled) : ICommand<Result<ConfigureVerificationCommandCommandResult>>;
