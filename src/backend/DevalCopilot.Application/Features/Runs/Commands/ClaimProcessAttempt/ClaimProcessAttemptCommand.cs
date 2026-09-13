using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Commands.ClaimProcessAttempt;

/// <summary>
/// Claims a run whose intent was already recorded and commits the durable, non-secret
/// execution intent for a Process attempt in the same transaction — before any external
/// process starts, the database already describes exactly what that attempt was going to run.
/// </summary>
public sealed record ClaimProcessAttemptCommand(Guid RunId, ProcessExecutionIntent Intent)
    : ICommand<Result<ClaimProcessAttemptCommandResult>>;
