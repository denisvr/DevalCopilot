using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Runs.Commands.RecoverLocalCommitOperations;

/// <summary>
/// Startup recovery of every non-terminal local-commit operation (ADR-0029), run before ordinary workspace reconciliation and
/// before any supervisor dispatch. It proves each outcome from the recorded commit, tree, parent, trailer, branch, ownership and
/// index facts; it never retries Git, searches for a plausible commit or invents a terminal decision. Returns the number of
/// operations it decided.
/// </summary>
public sealed record RecoverLocalCommitOperationsCommand : IManualTransactionCommand<Result<int>>;
