using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Runs.Commands.ArmLocalExecutionSequence;

/// <summary>
/// The one explicit Human authorization of a local execution sequence for one accepted plan (ADR-0060). The caller supplies only the
/// exact plan, workspace, checkpoint, fingerprint and the confirmation value of the availability it read; the host decides the mode,
/// lifecycle, project, recipes, settings, allowance, times and participants from fresh authority. Arming invokes no provider, runs no
/// verification, prepares no workspace, claims no attempt, consumes no budget and acquires no global slot. A replay of the identical
/// tuple returns the recorded receipt (even after the sequence ended) and never rearms; a different tuple conflicts. Manual
/// transaction: the fresh Git capture runs outside any transaction, and the handler owns the short write-locked transaction that
/// re-reads every authority fact and records the sequence, its pinned recipes and its event together.
/// </summary>
public sealed record ArmLocalExecutionSequenceCommand(
    Guid RunId,
    Guid PlanProposalMessageId,
    Guid WorkspaceId,
    Guid CheckpointId,
    string FingerprintSha256,
    string ConfirmationSha256) : IManualTransactionCommand<Result<ArmLocalExecutionSequenceCommandResult>>;
