using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Commands.RecordProcessAttemptResult;

/// <summary>
/// Records the terminal result of a claimed Process attempt in one short transaction, outside
/// whatever external execution produced it. A <see langword="null"/> <see cref="Outcome"/>
/// means the adapter or request reconstruction failed before producing any process result at
/// all — the attempt still fails, but with no outcome, exit code, or exception detail ever
/// persisted.
///
/// <paramref name="SealedArtifacts"/> carries zero, one, or two already-sealed and already-hashed
/// output files (stdout, stderr) to record as durable <see cref="Artifact"/> rows — atomically,
/// in the same transaction as the terminal state and a matching metadata-only
/// <c>process.output_captured</c> event per artifact. A stream whose seal failed is simply
/// absent from this list; that never blocks or alters the attempt's own terminal recording.
/// </summary>
public sealed record RecordProcessAttemptResultCommand(
    Guid RunId, Guid AttemptId, ProcessOutcome? Outcome, int? ExitCode, IReadOnlyList<SealedOutputArtifact> SealedArtifacts)
    : ICommand<Result<AttemptStatus>>;
