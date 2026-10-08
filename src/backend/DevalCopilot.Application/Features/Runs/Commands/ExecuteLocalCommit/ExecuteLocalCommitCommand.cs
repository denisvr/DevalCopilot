using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Runs.Commands.ExecuteLocalCommit;

/// <summary>
/// Hosted execution of one admitted local-commit operation (ADR-0029). It re-reads the complete authority at the execution seam,
/// records the single-use execution marker, performs the one host mutation outside any transaction and records the proven outcome.
/// Manual transaction: no EF transaction ever spans the Git work.
/// </summary>
public sealed record ExecuteLocalCommitCommand(Guid OperationId) : IManualTransactionCommand<Result<LocalCommitOperationView>>;
