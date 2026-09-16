using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Domain.Features.Projects;

namespace DevalCopilot.Application.Features.Projects.Commands.RecordVerificationExecutionResult;

public sealed record RecordVerificationExecutionResultCommand(
    Guid VerificationExecutionId,
    VerificationExecutionOutcome Outcome,
    int? ExitCode,
    string CompletionFingerprintSha256,
    IReadOnlyList<SealedVerificationOutputArtifact> SealedArtifacts) : ICommand<Result<VerificationExecutionStatus>>;
