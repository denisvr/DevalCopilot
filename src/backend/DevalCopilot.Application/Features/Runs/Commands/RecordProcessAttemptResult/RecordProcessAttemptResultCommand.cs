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
/// </summary>
public sealed record RecordProcessAttemptResultCommand(Guid RunId, Guid AttemptId, ProcessOutcome? Outcome, int? ExitCode)
    : ICommand<Result<AttemptStatus>>;
