namespace DevalCopilot.Domain.Features.Runs;

/// <summary>
/// The exact adapter contract versions of the two mutating Claude paths and what each can carry.
/// Version 1 is the historical invocation with no turn-limit override; version 2 is the same
/// invocation plus one optional, immutable agentic-turn limit. Version 3 (ADR-0072) carries exactly what
/// version 2 carries but reads only the schema-validated <c>structured_output</c> of the documented result
/// envelope, never the narrative result, and no longer passes the prompt-suggestion flag. Every newly
/// claimed attempt uses version 3 even when the limit is null; versions 1 and 2 remain historical,
/// classifiable facts that the current adapters never dispatch. A non-null limit is only ever coherent with
/// version 2 or 3 of its own path, so a request can never be silently dispatched without its flag.
/// </summary>
public static class ClaudeMutationAdapterContract
{
    public const string ImplementationV1 = "claude-implementation-v1";

    public const string ImplementationV2 = "claude-implementation-v2";

    public const string ImplementationV3 = "claude-implementation-v3";

    public const string ReviewCorrectionV1 = "claude-review-correction-v1";

    public const string ReviewCorrectionV2 = "claude-review-correction-v2";

    public const string ReviewCorrectionV3 = "claude-review-correction-v3";

    /// <summary>The current version a new initial implementation attempt is claimed with.</summary>
    public const string CurrentImplementation = ImplementationV3;

    /// <summary>The current version a new review or diagnosis correction attempt is claimed with.</summary>
    public const string CurrentReviewCorrection = ReviewCorrectionV3;

    /// <summary>Whether <paramref name="version"/> is an exact version 2 or 3 that carries the turn-limit option.</summary>
    public static bool CarriesTurnLimit(string? version) =>
        version is ImplementationV2 or ImplementationV3 or ReviewCorrectionV2 or ReviewCorrectionV3;

    /// <summary>Whether <paramref name="version"/> is the exact version 1, 2 or 3 of the path that owns
    /// <paramref name="responseContract"/> (an initial implementation report or a review correction).</summary>
    public static bool IsKnownVersionFor(AgentResponseContract? responseContract, string? version) => responseContract switch
    {
        AgentResponseContract.ImplementationReport => version is ImplementationV1 or ImplementationV2 or ImplementationV3,
        AgentResponseContract.ReviewCorrection => version is ReviewCorrectionV1 or ReviewCorrectionV2 or ReviewCorrectionV3,
        _ => false,
    };

    /// <summary>Whether <paramref name="version"/> is the exact current version of the path that owns
    /// <paramref name="responseContract"/>: the only version the current adapters may dispatch. A historical version
    /// is never upgraded or redispatched.</summary>
    public static bool IsCurrentVersionFor(AgentResponseContract? responseContract, string? version) => responseContract switch
    {
        AgentResponseContract.ImplementationReport => version == CurrentImplementation,
        AgentResponseContract.ReviewCorrection => version == CurrentReviewCorrection,
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

        var (first, second, third) = responseContract switch
        {
            AgentResponseContract.ImplementationReport => (ImplementationV1, ImplementationV2, ImplementationV3),
            AgentResponseContract.ReviewCorrection => (ReviewCorrectionV1, ReviewCorrectionV2, ReviewCorrectionV3),
            _ => (null, null, null),
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

        if (string.Equals(adapterContractVersion, second, StringComparison.Ordinal)
            || string.Equals(adapterContractVersion, third, StringComparison.Ordinal))
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

        var (first, second, third) = responseContract switch
        {
            AgentResponseContract.ImplementationReport => (ImplementationV1, ImplementationV2, ImplementationV3),
            AgentResponseContract.ReviewCorrection => (ReviewCorrectionV1, ReviewCorrectionV2, ReviewCorrectionV3),
            _ => (null, null, null),
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

        if (string.Equals(adapterContractVersion, second, StringComparison.Ordinal)
            || string.Equals(adapterContractVersion, third, StringComparison.Ordinal))
        {
            return requestedMaxTurns is null
                ? ClaudeMutationTurnLimitEvidence.NotRequested
                : ClaudeMutationTurnLimitEvidence.Requested;
        }

        return ClaudeMutationTurnLimitEvidence.Unknown;
    }
}
