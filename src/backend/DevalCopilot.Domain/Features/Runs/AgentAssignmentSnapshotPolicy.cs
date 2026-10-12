namespace DevalCopilot.Domain.Features.Runs;

/// <summary>
/// The one read-side rule for whether an Agent attempt's stored assignment facts form a well-formed immutable
/// snapshot, shared by <see cref="Attempt.GetAssignmentSnapshot"/> and by readers that judge the same stored facts
/// from bounded scalar columns without materializing an <see cref="Attempt"/>. It is extracted unchanged: the
/// identifier bound and shape, the provider, the exact turn-limit reading and the adapter contract version are judged
/// exactly as before, and this type adds no write, claim or dispatch rule.
/// </summary>
public static class AgentAssignmentSnapshotPolicy
{
    public const int MaximumIdentifierLength = 128;

    /// <summary>An assignment identifier is absent, or nonblank and at most <see cref="MaximumIdentifierLength"/> characters.</summary>
    public static bool IsValidIdentifier(string? value) =>
        value is null || (!string.IsNullOrWhiteSpace(value) && value.Length <= MaximumIdentifierLength);

    /// <summary>Returns the snapshot of the given stored facts, or <see langword="null"/> when they are not a well-formed
    /// Agent assignment: not an Agent attempt, no defined provider, an invalid identifier, a malformed turn-limit
    /// reading, an invalid adapter contract version or an undefined permission profile. An absent permission profile is
    /// <see cref="AgentPermissionProfile.Unknown"/>.</summary>
    public static AgentAssignmentSnapshot? Read(
        AttemptKind kind,
        AgentProvider? provider,
        string? requestedModel,
        string? observedModel,
        string? requestedEffort,
        string? observedEffort,
        AgentPermissionProfile? permissionProfile,
        string? adapterContractVersion,
        ClaudeMutationTurnLimitReading requestedMaxTurns)
    {
        if (kind != AttemptKind.Agent
            || provider is not { } definedProvider
            || !Enum.IsDefined(definedProvider)
            || !IsValidIdentifier(requestedModel)
            || !IsValidIdentifier(observedModel)
            || !IsValidIdentifier(requestedEffort)
            || !IsValidIdentifier(observedEffort)
            || requestedMaxTurns.IsMalformed
            || (adapterContractVersion is { } contractVersion
                && (string.IsNullOrWhiteSpace(contractVersion) || contractVersion.Length > MaximumIdentifierLength)))
        {
            return null;
        }

        var profile = permissionProfile ?? AgentPermissionProfile.Unknown;
        if (!Enum.IsDefined(profile))
        {
            return null;
        }

        return new AgentAssignmentSnapshot(
            definedProvider, requestedModel, observedModel, requestedEffort, observedEffort, profile, adapterContractVersion,
            requestedMaxTurns.Value);
    }
}
