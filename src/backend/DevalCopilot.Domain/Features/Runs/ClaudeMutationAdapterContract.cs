namespace DevalCopilot.Domain.Features.Runs;

/// <summary>
/// The exact adapter contract versions of the two mutating Claude paths and what each can carry.
/// Version 1 is the historical invocation with no turn-limit override; version 2 is the same
/// invocation plus one optional, immutable agentic-turn limit. Every newly claimed attempt uses
/// version 2 even when the limit is null, and a non-null limit is only ever coherent with the exact
/// version 2 of its own path, so a request can never be silently dispatched without its flag.
/// </summary>
public static class ClaudeMutationAdapterContract
{
    public const string ImplementationV1 = "claude-implementation-v1";

    public const string ImplementationV2 = "claude-implementation-v2";

    public const string ReviewCorrectionV1 = "claude-review-correction-v1";

    public const string ReviewCorrectionV2 = "claude-review-correction-v2";

    /// <summary>Whether <paramref name="version"/> is the exact version 2 that carries the turn-limit option.</summary>
    public static bool CarriesTurnLimit(string? version) => version is ImplementationV2 or ReviewCorrectionV2;

    /// <summary>Whether <paramref name="version"/> is the exact version 1 or 2 of the path that owns
    /// <paramref name="responseContract"/> (an initial implementation report or a review correction).</summary>
    public static bool IsKnownVersionFor(AgentResponseContract? responseContract, string? version) => responseContract switch
    {
        AgentResponseContract.ImplementationReport => version is ImplementationV1 or ImplementationV2,
        AgentResponseContract.ReviewCorrection => version is ReviewCorrectionV1 or ReviewCorrectionV2,
        _ => false,
    };

    /// <summary>Classifies a stored attempt's turn-limit facts against its own response contract, role,
    /// provider, permission profile, and adapter contract version. Exact, version-aware mappings only:
    /// an attempt that is not a Claude mutation attempt records no limit and yields
    /// <see cref="ClaudeMutationTurnLimitEvidence.NotRecorded"/>; any disagreement yields
    /// <see cref="ClaudeMutationTurnLimitEvidence.Unknown"/>.</summary>
    public static ClaudeMutationTurnLimitEvidence Classify(
        AgentResponseContract? responseContract,
        AgentRole? role,
        AgentProvider? provider,
        AgentPermissionProfile? permissionProfile,
        string? adapterContractVersion,
        int? requestedMaxTurns) => Classify(
            responseContract, role, provider, permissionProfile, adapterContractVersion,
            ClaudeMutationTurnLimit.IsValid(requestedMaxTurns)
                ? new ClaudeMutationTurnLimitReading(false, requestedMaxTurns)
                : ClaudeMutationTurnLimitReading.Malformed);

    /// <summary>Whether a provider may be invoked for an attempt with this stored turn-limit reading. An attempt
    /// that recorded no request is unaffected (historical v1/null and v2/null dispatch eligibility is unchanged); a
    /// malformed reading is never dispatchable; and a recorded request needs the complete coherent tuple: the
    /// Claude provider, the Implementer role, the response contract of a mutation path, the workspace-edit
    /// permission profile, and that path's exact version 2 adapter contract. Anything else fails closed here, before
    /// any process could start, because the invocation request carries only the cap and the version.</summary>
    public static bool IsDispatchCoherent(
        AgentResponseContract? responseContract,
        AgentRole? role,
        AgentProvider? provider,
        AgentPermissionProfile? permissionProfile,
        string? adapterContractVersion,
        ClaudeMutationTurnLimitReading reading) =>
        reading.IsMalformed
            ? false
            : reading.Value is null
                || Classify(responseContract, role, provider, permissionProfile, adapterContractVersion, reading)
                    == ClaudeMutationTurnLimitEvidence.Requested;

    /// <summary>Classifies a stored attempt's direct-guidance snapshot against its own response contract, role, provider,
    /// permission profile, and adapter contract version, by the same exact version-aware mapping as the turn limit. A
    /// coherent attempt with no guidance is <see cref="DirectHumanGuidanceEvidence.NotRecorded"/> whatever its contract
    /// version (a null snapshot says nothing about submission history), a coherent v2 attempt with text is
    /// <see cref="DirectHumanGuidanceEvidence.Provided"/>, and any disagreement or malformed text is
    /// <see cref="DirectHumanGuidanceEvidence.Unknown"/>.</summary>
    public static DirectHumanGuidanceEvidence ClassifyDirectGuidance(
        AgentResponseContract? responseContract,
        AgentRole? role,
        AgentProvider? provider,
        AgentPermissionProfile? permissionProfile,
        string? adapterContractVersion,
        DirectHumanGuidanceReading reading)
    {
        if (reading.IsMalformed)
        {
            return DirectHumanGuidanceEvidence.Unknown;
        }

        var (first, second) = responseContract switch
        {
            AgentResponseContract.ImplementationReport => (ImplementationV1, ImplementationV2),
            AgentResponseContract.ReviewCorrection => (ReviewCorrectionV1, ReviewCorrectionV2),
            _ => (null, null),
        };

        if (first is null)
        {
            return reading.Text is null ? DirectHumanGuidanceEvidence.NotRecorded : DirectHumanGuidanceEvidence.Unknown;
        }

        if (role != AgentRole.Implementer
            || provider != AgentProvider.ClaudeCode
            || permissionProfile != AgentPermissionProfile.WorkspaceEditOnly)
        {
            return DirectHumanGuidanceEvidence.Unknown;
        }

        if (string.Equals(adapterContractVersion, first, StringComparison.Ordinal))
        {
            return reading.Text is null ? DirectHumanGuidanceEvidence.NotRecorded : DirectHumanGuidanceEvidence.Unknown;
        }

        if (string.Equals(adapterContractVersion, second, StringComparison.Ordinal))
        {
            return reading.Text is null ? DirectHumanGuidanceEvidence.NotRecorded : DirectHumanGuidanceEvidence.Provided;
        }

        return DirectHumanGuidanceEvidence.Unknown;
    }

    /// <summary>Whether a provider may be invoked for an attempt with this direct-guidance reading. An attempt that
    /// recorded none is unaffected (historical unguided and authorized attempts dispatch as before); malformed text is
    /// never dispatchable; and recorded guidance needs the complete coherent tuple, including that path's exact version 2.</summary>
    public static bool IsDirectGuidanceDispatchCoherent(
        AgentResponseContract? responseContract,
        AgentRole? role,
        AgentProvider? provider,
        AgentPermissionProfile? permissionProfile,
        string? adapterContractVersion,
        DirectHumanGuidanceReading reading) =>
        reading.IsMalformed
            ? false
            : reading.Text is null
                || ClassifyDirectGuidance(responseContract, role, provider, permissionProfile, adapterContractVersion, reading)
                    == DirectHumanGuidanceEvidence.Provided;

    /// <summary>The same classification over an exact stored-text reading: a malformed reading is always
    /// <see cref="ClaudeMutationTurnLimitEvidence.Unknown"/>.</summary>
    public static ClaudeMutationTurnLimitEvidence Classify(
        AgentResponseContract? responseContract,
        AgentRole? role,
        AgentProvider? provider,
        AgentPermissionProfile? permissionProfile,
        string? adapterContractVersion,
        ClaudeMutationTurnLimitReading reading)
    {
        if (reading.IsMalformed)
        {
            return ClaudeMutationTurnLimitEvidence.Unknown;
        }

        var requestedMaxTurns = reading.Value;

        var (first, second) = responseContract switch
        {
            AgentResponseContract.ImplementationReport => (ImplementationV1, ImplementationV2),
            AgentResponseContract.ReviewCorrection => (ReviewCorrectionV1, ReviewCorrectionV2),
            _ => (null, null),
        };

        if (first is null)
        {
            return requestedMaxTurns is null
                ? ClaudeMutationTurnLimitEvidence.NotRecorded
                : ClaudeMutationTurnLimitEvidence.Unknown;
        }

        if (role != AgentRole.Implementer
            || provider != AgentProvider.ClaudeCode
            || permissionProfile != AgentPermissionProfile.WorkspaceEditOnly)
        {
            return ClaudeMutationTurnLimitEvidence.Unknown;
        }

        if (string.Equals(adapterContractVersion, first, StringComparison.Ordinal))
        {
            return requestedMaxTurns is null
                ? ClaudeMutationTurnLimitEvidence.NotRecorded
                : ClaudeMutationTurnLimitEvidence.Unknown;
        }

        if (string.Equals(adapterContractVersion, second, StringComparison.Ordinal))
        {
            return requestedMaxTurns is null
                ? ClaudeMutationTurnLimitEvidence.NotRequested
                : ClaudeMutationTurnLimitEvidence.Requested;
        }

        return ClaudeMutationTurnLimitEvidence.Unknown;
    }
}
