using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Projects.Commands.RecoverInterruptedVerificationOutputArtifacts;

public sealed record RecoverInterruptedVerificationOutputArtifactsCommand
    : IManualTransactionCommand<Result<RecoverInterruptedVerificationOutputArtifactsCommandResult>>;

/// <summary>ADR-0072: how many sealed outputs were imported, and whether any single historical output file could not be read
/// (an optional-output condition only; a database fault never becomes this flag and fails the command).</summary>
public sealed record RecoverInterruptedVerificationOutputArtifactsCommandResult(int ImportedCount, bool OptionalOutputDeferred);
