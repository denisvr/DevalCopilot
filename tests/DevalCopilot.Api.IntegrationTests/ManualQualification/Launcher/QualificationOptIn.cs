namespace DevalCopilot.Api.IntegrationTests.ManualQualification;

/// <summary>
/// The deliberate opt-in of lot B without which nothing starts: no host, no provider, no directory and no ledger entry. It needs the
/// one fixed flag, optionally followed by exactly one <see cref="ApprovedLaunchManifest.Option"/> and its path (the production entry
/// requires it and refuses before anything exists without it), and the exact confirmation phrase in the environment, so neither an
/// ambient variable nor an accidental invocation can enable a real-provider session. The flag and phrase belong to the one
/// review-schema successor authorization of QUALIFICATION-PROFILE-01 only: the first, the context-bound, the Windows-root and the
/// proposal-guidance lot B flags and phrases are retired and the earlier session flag is no longer recognized, so no invocation can
/// reach an earlier authorization.
/// </summary>
public static class QualificationOptIn
{
    public const string Flag = "--run-qualification-profile-01-review-schema-exchange";
    public const string ConfirmationVariable = "DEVALCOPILOT_MANUAL_QUALIFICATION_CONFIRM";
    public const string ConfirmationPhrase = "qualification-profile-01-one-review-schema-codex-planner-and-claude-reviewer";

    /// <summary>The flag of the first lot B entry, whose allowance and process slots were consumed. Named only so that it can be
    /// refused: it never parses, never starts a process and never selects a ledger.</summary>
    public const string RetiredFlag = "--run-qualification-profile-01-exchange";

    /// <summary>The flag of the context-bound lot B entry, whose authorization was spent by one native Codex Planner exit. Named only
    /// so that it can be refused: it never parses, never starts a process and never selects a ledger.</summary>
    public const string RetiredContextBoundFlag = "--run-qualification-profile-01-context-bound-exchange";

    /// <summary>The flag of the Windows-root lot B entry, whose authorization was spent by one native Codex Planner exit whose sealed
    /// response the parser refused. Named only so that it can be refused: it never parses, never starts a process and never selects
    /// a ledger.</summary>
    public const string RetiredWindowsRootFlag = "--run-qualification-profile-01-windows-root-exchange";

    /// <summary>The flag of the proposal-guidance lot B entry, whose one allowance pair was used by an accepted Codex Proposal and a
    /// Claude reviewer exit with a provider-reported schema error and no review. Named only so that it can be refused: it never
    /// parses, never starts a process and never selects a ledger.</summary>
    public const string RetiredProposalGuidanceFlag = "--run-qualification-profile-01-proposal-guidance-exchange";

    public static bool IsGranted(IReadOnlyList<string> arguments, Func<string, string?> readEnvironment) =>
        arguments.Count is 1 or 3
        && string.Equals(arguments[0], Flag, StringComparison.Ordinal)
        && (arguments.Count == 1
            || (string.Equals(arguments[1], ApprovedLaunchManifest.Option, StringComparison.Ordinal) && arguments[2].Length > 0))
        && string.Equals(readEnvironment(ConfirmationVariable), ConfirmationPhrase, StringComparison.Ordinal);

    /// <summary>True when the first argument is exactly one of the retired lot B flags, whatever follows it.</summary>
    public static bool IsRetiredFlagArgument(IReadOnlyList<string> arguments) =>
        arguments.Count > 0
        && (string.Equals(arguments[0], RetiredFlag, StringComparison.Ordinal)
            || string.Equals(arguments[0], RetiredContextBoundFlag, StringComparison.Ordinal)
            || string.Equals(arguments[0], RetiredWindowsRootFlag, StringComparison.Ordinal)
            || string.Equals(arguments[0], RetiredProposalGuidanceFlag, StringComparison.Ordinal));

    /// <summary>The path that followed the manifest option, or null when none was given.</summary>
    public static string? ApprovedLaunchesPath(IReadOnlyList<string> arguments) =>
        arguments.Count == 3 ? arguments[2] : null;
}
