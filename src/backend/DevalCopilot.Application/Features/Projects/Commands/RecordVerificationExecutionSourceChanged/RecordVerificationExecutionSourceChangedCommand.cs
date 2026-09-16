using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Projects.Commands.RecordVerificationExecutionSourceChanged;

public sealed record RecordVerificationExecutionSourceChangedCommand(Guid VerificationExecutionId, string CompletionFingerprintSha256)
    : ICommand<Result>;
