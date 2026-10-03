using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Features.Projects.Commands.MarkVerificationExecutionDispatched;

namespace DevalCopilot.Application.Features.Projects.Commands.RecordVerificationExecutionSourceChanged;

/// <summary>Manual transaction: a pre-dispatch SourceChanged is recorded only when the durable execution and its ownership still
/// agree with the snapshot the observation was taken against.</summary>
public sealed record RecordVerificationExecutionSourceChangedCommand(
    Guid VerificationExecutionId, string CompletionFingerprintSha256, VerificationDispatchSnapshot Expected)
    : IManualTransactionCommand<Result>;
