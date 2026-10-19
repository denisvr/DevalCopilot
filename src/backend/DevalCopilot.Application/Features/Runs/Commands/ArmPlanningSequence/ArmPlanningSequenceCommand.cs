using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Runs.Commands.ArmPlanningSequence;

/// <summary>
/// The one explicit Human authorization of a read-only planning sequence for a pristine manual run (ADR-0055). The caller supplies only
/// the exact workspace, checkpoint and fingerprint it saw; the host decides the mode, lifecycle, project, objective binding, times and
/// participants from fresh authority. Arming invokes no provider, prepares no workspace and consumes no Agent claim or time budget. A
/// replay of the identical tuple returns the recorded receipt (even after the sequence ended) and never rearms; a different tuple
/// conflicts. Manual transaction: the fresh Git capture runs outside any transaction, and the handler owns the short write-locked
/// transaction that re-reads every authority fact and records the sequence and its event together.
/// </summary>
public sealed record ArmPlanningSequenceCommand(Guid RunId, Guid WorkspaceId, Guid CheckpointId, string FingerprintSha256)
    : IManualTransactionCommand<Result<ArmPlanningSequenceCommandResult>>;
