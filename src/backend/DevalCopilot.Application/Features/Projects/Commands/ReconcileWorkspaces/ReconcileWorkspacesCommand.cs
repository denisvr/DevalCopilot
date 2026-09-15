using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Projects.Commands.ReconcileWorkspaces;

/// <summary>
/// Run once at startup, before any new preparation request is accepted — never on a timer or
/// poll loop. Declared <see cref="IManualTransactionCommand{TResult}"/>, not
/// <see cref="ICommand{TResult}"/>: this handler evaluates real Git/marker evidence for
/// potentially several leases in one pass, and wrapping the whole pass in one ambient
/// transaction (<c>AddDevalenteEfCoreTransactions</c>'s automatic behavior for an ordinary
/// <see cref="ICommand{TResult}"/>) would hold that transaction open across all of that external
/// I/O and would let one lease's failure roll back an unrelated, already-decided lease's
/// transition. <c>ReconcileWorkspacesCommandHandler</c> instead commits each lease's own
/// transition — if any — in its own short, independent <c>SaveChangesAsync</c> immediately after
/// deciding it, with no EF transaction open while the external evidence for that lease (or any
/// other) is gathered. See ADR-0008.
/// </summary>
public sealed record ReconcileWorkspacesCommand : IManualTransactionCommand<Result<int>>;
