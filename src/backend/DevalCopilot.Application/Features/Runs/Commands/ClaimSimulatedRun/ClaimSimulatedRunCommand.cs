using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Runs.Commands.ClaimSimulatedRun;

/// <summary>
/// Claims a run whose intent was already recorded. Committing this before the simulated
/// adapter runs is the durable-intent invariant: a new attempt is committed before
/// external work starts.
/// </summary>
public sealed record ClaimSimulatedRunCommand(Guid RunId) : ICommand<Result<ClaimSimulatedRunCommandResult>>;
