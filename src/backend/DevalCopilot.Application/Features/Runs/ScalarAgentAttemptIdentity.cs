using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs;

/// <summary>
/// The bounded, untracked scalar projection of one attempt and the evaluation that applies the unchanged assignment read policy and
/// identity rule to it (ADR-0037, extracted unchanged for ADR-0051). Nothing outside the listed columns is ever selected: no prompt, path,
/// session, artifact, checkpoint, outcome, token or guidance column, and no stored enum is materialized. Each caller keeps its own
/// further checks (the attribution reader compares the identity with its message's actor).
/// </summary>
internal static class ScalarAgentAttemptIdentity
{
    private const int Absent = 0;
    private const int Unrecognized = 99;

    // A UTF-16 unit is at most three UTF-8 bytes, so a valid identifier of at most the Domain bound of 128 characters always fits; the
    // exact character bound is applied again in memory. length() of TEXT stops at an embedded NUL, so the guards bound the stored bytes
    // (CAST AS BLOB). A valid stored turn limit is at most three digits, so a stored value past a small bound is malformed storage.
    private const int IdentifierByteBound = AgentAssignmentSnapshotPolicy.MaximumIdentifierLength * 3;
    private const int MaxTurnsByteBound = 64;

    private static readonly AttemptStatus[] StatusesByCode =
        [AttemptStatus.Running, AttemptStatus.Completed, AttemptStatus.Failed, AttemptStatus.Interrupted];

    private static readonly AgentRole[] RolesByCode =
        [AgentRole.Planner, AgentRole.CriticalReviewer, AgentRole.Resolver, AgentRole.Implementer, AgentRole.CodeReviewer];

    private static readonly AgentProvider[] ProvidersByCode = [AgentProvider.Codex, AgentProvider.ClaudeCode];

    private static readonly AgentResponseContract[] ContractsByCode =
    [
        AgentResponseContract.Proposal,
        AgentResponseContract.CriticalReview,
        AgentResponseContract.ChallengeResolution,
        AgentResponseContract.ImplementationReport,
        AgentResponseContract.ImplementationReview,
        AgentResponseContract.ReviewCorrection,
        AgentResponseContract.VerificationDiagnosis,
    ];

    private static readonly AgentPermissionProfile[] ProfilesByCode =
        [AgentPermissionProfile.Unknown, AgentPermissionProfile.WorkspaceEditOnly, AgentPermissionProfile.ReadOnly];

    public static IQueryable<ScalarAgentAttemptRow> ProjectScalarIdentity(this IQueryable<Attempt> attempts) =>
        attempts.Select(attempt => new ScalarAgentAttemptRow(
            attempt.Id,
            attempt.AttemptNumber,
            attempt.ClaimedAtUtc,
            attempt.CompletedAtUtc,
            attempt.AgentDispatchedAtUtc,
            attempt.Kind == AttemptKind.Agent,
            attempt.Status == AttemptStatus.Running ? 1
                : attempt.Status == AttemptStatus.Completed ? 2
                : attempt.Status == AttemptStatus.Failed ? 3
                : attempt.Status == AttemptStatus.Interrupted ? 4
                : Unrecognized,
            attempt.AgentRole == null ? Absent
                : attempt.AgentRole == AgentRole.Planner ? 1
                : attempt.AgentRole == AgentRole.CriticalReviewer ? 2
                : attempt.AgentRole == AgentRole.Resolver ? 3
                : attempt.AgentRole == AgentRole.Implementer ? 4
                : attempt.AgentRole == AgentRole.CodeReviewer ? 5
                : Unrecognized,
            attempt.AgentProvider == null ? Absent
                : attempt.AgentProvider == AgentProvider.Codex ? 1
                : attempt.AgentProvider == AgentProvider.ClaudeCode ? 2
                : Unrecognized,
            attempt.AgentResponseContract == null ? Absent
                : attempt.AgentResponseContract == AgentResponseContract.Proposal ? 1
                : attempt.AgentResponseContract == AgentResponseContract.CriticalReview ? 2
                : attempt.AgentResponseContract == AgentResponseContract.ChallengeResolution ? 3
                : attempt.AgentResponseContract == AgentResponseContract.ImplementationReport ? 4
                : attempt.AgentResponseContract == AgentResponseContract.ImplementationReview ? 5
                : attempt.AgentResponseContract == AgentResponseContract.ReviewCorrection ? 6
                : attempt.AgentResponseContract == AgentResponseContract.VerificationDiagnosis ? 7
                : Unrecognized,
            attempt.AgentPermissionProfile == null ? Absent
                : attempt.AgentPermissionProfile == AgentPermissionProfile.Unknown ? 1
                : attempt.AgentPermissionProfile == AgentPermissionProfile.WorkspaceEditOnly ? 2
                : attempt.AgentPermissionProfile == AgentPermissionProfile.ReadOnly ? 3
                : Unrecognized,
            ((byte[])(object)attempt.AgentRequestedModel!).Length <= IdentifierByteBound ? attempt.AgentRequestedModel : null,
            attempt.AgentRequestedModel != null && ((byte[])(object)attempt.AgentRequestedModel).Length > IdentifierByteBound,
            ((byte[])(object)attempt.AgentObservedModel!).Length <= IdentifierByteBound ? attempt.AgentObservedModel : null,
            attempt.AgentObservedModel != null && ((byte[])(object)attempt.AgentObservedModel).Length > IdentifierByteBound,
            ((byte[])(object)attempt.AgentRequestedEffort!).Length <= IdentifierByteBound ? attempt.AgentRequestedEffort : null,
            attempt.AgentRequestedEffort != null && ((byte[])(object)attempt.AgentRequestedEffort).Length > IdentifierByteBound,
            ((byte[])(object)attempt.AgentObservedEffort!).Length <= IdentifierByteBound ? attempt.AgentObservedEffort : null,
            attempt.AgentObservedEffort != null && ((byte[])(object)attempt.AgentObservedEffort).Length > IdentifierByteBound,
            ((byte[])(object)attempt.AgentAdapterContractVersion!).Length <= IdentifierByteBound ? attempt.AgentAdapterContractVersion : null,
            attempt.AgentAdapterContractVersion != null && ((byte[])(object)attempt.AgentAdapterContractVersion).Length > IdentifierByteBound,
            ((byte[])(object)EF.Property<string?>(attempt, Attempt.AgentRequestedMaxTurnsStorageProperty)!).Length <= MaxTurnsByteBound
                ? EF.Property<string?>(attempt, Attempt.AgentRequestedMaxTurnsStorageProperty)
                : null,
            EF.Property<string?>(attempt, Attempt.AgentRequestedMaxTurnsStorageProperty) != null
                && ((byte[])(object)EF.Property<string?>(attempt, Attempt.AgentRequestedMaxTurnsStorageProperty)!).Length > MaxTurnsByteBound));

    /// <summary>The coherent recorded identity of a projected attempt, or null for every stored shape that cannot be proved: an empty
    /// identity, a nonpositive number, a non-Agent kind, an unknown or absent status, role, provider or contract, a rejected oversized
    /// assignment storage, or an assignment or identity the unchanged policy and rule reject.</summary>
    public static CoherentAgentAttemptIdentity? Evaluate(ScalarAgentAttemptRow attempt)
    {
        if (attempt.Id == Guid.Empty || attempt.AttemptNumber <= 0 || !attempt.IsAgent)
        {
            return null;
        }

        AttemptStatus? status = attempt.StatusCode is >= 1 and <= 4 ? StatusesByCode[attempt.StatusCode - 1] : null;
        AgentRole? role = attempt.RoleCode is >= 1 and <= 5 ? RolesByCode[attempt.RoleCode - 1] : null;
        AgentProvider? provider = attempt.ProviderCode is >= 1 and <= 2 ? ProvidersByCode[attempt.ProviderCode - 1] : null;
        AgentResponseContract? contract = attempt.ContractCode is >= 1 and <= 7 ? ContractsByCode[attempt.ContractCode - 1] : null;
        if (role is null || provider is null || status is null || contract is null)
        {
            return null;
        }

        // A rejected oversized storage is never read as a legitimate null: it is invalid by itself.
        if (attempt.RequestedModelOversized
            || attempt.ObservedModelOversized
            || attempt.RequestedEffortOversized
            || attempt.ObservedEffortOversized
            || attempt.AdapterContractVersionOversized)
        {
            return null;
        }

        // A stored profile that is not a defined member is passed as such, so the unchanged policy rejects it.
        AgentPermissionProfile? profile = attempt.ProfileCode switch
        {
            0 => null,
            >= 1 and <= 3 => ProfilesByCode[attempt.ProfileCode - 1],
            _ => (AgentPermissionProfile)(-1),
        };
        var maxTurns = attempt.StoredMaxTurnsOversized
            ? ClaudeMutationTurnLimitReading.Malformed
            : ClaudeMutationTurnLimit.Read(attempt.StoredMaxTurns);
        var assignment = AgentAssignmentSnapshotPolicy.Read(
            AttemptKind.Agent,
            provider,
            attempt.RequestedModel,
            attempt.ObservedModel,
            attempt.RequestedEffort,
            attempt.ObservedEffort,
            profile,
            attempt.AdapterContractVersion,
            maxTurns);
        return AgentAttemptIdentity.IsCoherent(AttemptKind.Agent, status, role, provider, contract, assignment)
            ? new CoherentAgentAttemptIdentity(status.Value, role.Value, provider.Value, contract.Value)
            : null;
    }
}
