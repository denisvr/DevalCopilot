using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Runs.Commands.ReleaseLocalExecutionOwnership;

/// <summary>
/// The Human decision to release the retained ownership quarantine of one terminal local execution operation after durable original-host
/// termination evidence (ADR-0065). The four values are exactly what the Human saw: the observation, the confirmation version and digest
/// and the UTC expiry shown with them. The host recomputes everything from fresh facts under its own write lock and refuses a stale,
/// mismatched or expired candidate without retargeting it; an identical replay returns the recorded receipt with no write and no event.
/// It changes no operation outcome, cycle, workspace, lease, approval, artifact or budget. Manual transaction: the handler owns the short
/// write-locked transaction and performs no external I/O.
/// </summary>
public sealed record ReleaseLocalExecutionOwnershipCommand(
    Guid RunId,
    Guid StepId,
    Guid ObservationId,
    int ConfirmationVersion,
    string ConfirmationSha256,
    DateTimeOffset ConfirmationExpiresAtUtc)
    : IManualTransactionCommand<Result<ReleaseLocalExecutionOwnershipCommandResult>>;
