using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Projects.Commands.PrepareRepositoryWorkspace;

/// <summary>
/// Declared <see cref="IManualTransactionCommand{TResult}"/>, not <see cref="ICommand{TResult}"/>
/// — the same precedent as <c>RecordSimulatedAgentStepCommand</c>/<c>CompleteSimulatedRunCommand</c>.
/// The pipeline's automatic per-command transaction wrap (<c>AddDevalenteEfCoreTransactions</c>)
/// applies only to <see cref="ICommand{TResult}"/>; wrapping this handler in one ambient
/// transaction would keep its durable-intent <c>SaveChangesAsync</c> uncommitted for the entire
/// duration of the external Git/marker I/O that follows it, silently reintroducing exactly the
/// crash-unsafe window ADR-0008 exists to close. Because this is a manual-transaction command,
/// <see cref="PrepareRepositoryWorkspaceCommandHandler"/> owns its own transaction boundaries: it
/// alone decides when each of its two explicit <c>SaveChangesAsync</c> calls commits.
/// </summary>
public sealed record PrepareRepositoryWorkspaceCommand(Guid ProjectId)
    : IManualTransactionCommand<Result<PrepareRepositoryWorkspaceCommandResult>>;
