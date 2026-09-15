using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Projects.Commands.UpdateVerificationCommand;

public sealed record UpdateVerificationCommandCommand(
    Guid ProjectId,
    Guid VerificationCommandId,
    string Name,
    string ExecutablePath,
    IReadOnlyCollection<string> Arguments,
    int TimeoutSeconds,
    bool IsEnabled) : ICommand<Result>;
