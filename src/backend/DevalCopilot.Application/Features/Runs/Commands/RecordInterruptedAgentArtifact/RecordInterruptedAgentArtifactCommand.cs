using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Commands.RecordInterruptedAgentArtifact;

/// <summary>
/// Imports one already-sealed, already-hashed Agent-attempt output file — recovered by restart
/// reconciliation from a host crash — as a durable <see cref="Artifact"/> row with
/// <see cref="ArtifactCaptureOutcome.PartialHostInterrupted"/>, plus a matching metadata-only
/// event. Idempotent: an explicit existence check plus a unique database constraint backstop
/// ensure a later restart never records the same (attempt, purpose) twice. Never valid for
/// <see cref="ArtifactPurpose.AgentContextManifest"/> — that artifact is sealed at claim time,
/// before dispatch, and is never a candidate for this recovery path.
/// </summary>
public sealed record RecordInterruptedAgentArtifactCommand(
    Guid RunId, Guid AttemptId, ArtifactPurpose Purpose, string RelativeStoragePath, long ByteLength, string ContentHash)
    : ICommand<Result<bool>>;
