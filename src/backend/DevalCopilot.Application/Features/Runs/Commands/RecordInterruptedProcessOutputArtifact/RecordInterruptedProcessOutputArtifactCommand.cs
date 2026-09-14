using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Commands.RecordInterruptedProcessOutputArtifact;

/// <summary>
/// Imports one already-sealed, already-hashed output file — recovered by restart reconciliation
/// from a host crash — as a durable <see cref="Artifact"/> row with
/// <see cref="ArtifactCaptureOutcome.PartialHostInterrupted"/>, plus a matching metadata-only
/// event. Idempotent by design: recovery may run this same call again on a later restart before
/// or after a prior attempt's own transaction failed, and it must never produce a duplicate row
/// for the same (attempt, purpose) — enforced by an explicit existence check here and a unique
/// database constraint as a backstop.
/// </summary>
public sealed record RecordInterruptedProcessOutputArtifactCommand(
    Guid RunId, Guid AttemptId, ArtifactPurpose Purpose, string RelativeStoragePath, long ByteLength, string ContentHash)
    : ICommand<Result<bool>>;
