using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Projects.Commands.ReconcileInterruptedVerificationExecutions;

public sealed record ReconcileInterruptedVerificationExecutionsCommand : ICommand<Result<int>>;
