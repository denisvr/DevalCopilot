using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Projects.Commands.MarkVerificationExecutionDispatched;

public sealed record MarkVerificationExecutionDispatchedCommand(Guid VerificationExecutionId) : ICommand<Result>;
