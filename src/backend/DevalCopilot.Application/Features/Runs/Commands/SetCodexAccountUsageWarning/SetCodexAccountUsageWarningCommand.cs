using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Runs.Commands.SetCodexAccountUsageWarning;

/// <summary>
/// Sets or clears the owner's optional, run-scoped advisory Codex account-usage warning (ADR-0026): a used-percent threshold from 1
/// through 100 that an explicit, separate check compares with one strict observation. <see cref="Percent"/> <see langword="null"/>
/// clears it. It contacts no provider, refuses, reserves and stops nothing, and is independent of the account-usage stop. Editable
/// only for a Created or Running run whose execution mode admits Agent work.
///
/// <para>
/// Manual transaction: the handler's single <c>SaveChangesAsync</c> owns the implicit database transaction that commits the Run
/// change and its event together, so a failed concurrency-checked UPDATE can never leave the queued event behind.
/// </para>
/// </summary>
public sealed record SetCodexAccountUsageWarningCommand(Guid RunId, int? Percent)
    : IManualTransactionCommand<Result<SetCodexAccountUsageWarningCommandResult>>;
