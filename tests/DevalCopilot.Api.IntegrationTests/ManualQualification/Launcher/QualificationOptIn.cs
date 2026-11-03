namespace DevalCopilot.Api.IntegrationTests.ManualQualification;

/// <summary>
/// The deliberate opt-in of lot B without which nothing starts: no host, no provider, no directory and no ledger entry. It needs the
/// one fixed flag, optionally followed by exactly one <see cref="ApprovedLaunchManifest.Option"/> and its path (the production entry
/// requires it and refuses before anything exists without it), and the exact confirmation phrase in the environment, so neither an
/// ambient variable nor an accidental invocation can enable a real-provider session. The flag and phrase belong to the one
/// context-bound replacement authorization of QUALIFICATION-PROFILE-01 only: the earlier lot B flag and phrase are retired and the
/// earlier session flag is no longer recognized, so no invocation can reach an earlier authorization.
/// </summary>
public static class QualificationOptIn
{
    public const string Flag = "--run-qualification-profile-01-context-bound-exchange";
    public const string ConfirmationVariable = "DEVALCOPILOT_MANUAL_QUALIFICATION_CONFIRM";
    public const string ConfirmationPhrase = "qualification-profile-01-one-context-bound-codex-planner-and-claude-reviewer";

    /// <summary>The flag of the first lot B entry, whose allowance and process slots were consumed. Named only so that it can be
    /// refused: it never parses, never starts a process and never selects a ledger.</summary>
    public const string RetiredFlag = "--run-qualification-profile-01-exchange";

    public static bool IsGranted(IReadOnlyList<string> arguments, Func<string, string?> readEnvironment) =>
        arguments.Count is 1 or 3
        && string.Equals(arguments[0], Flag, StringComparison.Ordinal)
        && (arguments.Count == 1
            || (string.Equals(arguments[1], ApprovedLaunchManifest.Option, StringComparison.Ordinal) && arguments[2].Length > 0))
        && string.Equals(readEnvironment(ConfirmationVariable), ConfirmationPhrase, StringComparison.Ordinal);

    /// <summary>The path that followed the manifest option, or null when none was given.</summary>
    public static string? ApprovedLaunchesPath(IReadOnlyList<string> arguments) =>
        arguments.Count == 3 ? arguments[2] : null;
}
