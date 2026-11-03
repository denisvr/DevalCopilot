namespace DevalCopilot.Api.IntegrationTests.ManualQualification;

/// <summary>
/// The deliberate opt-in of lot B without which nothing starts: no host, no provider, no directory and no ledger entry. It needs the
/// one fixed flag, optionally followed by exactly one <see cref="ApprovedLaunchManifest.Option"/> and its path (the production entry
/// requires it and refuses before anything exists without it), and the exact confirmation phrase in the environment, so neither an
/// ambient variable nor an accidental invocation can enable a real-provider session. The flag and phrase belong to
/// QUALIFICATION-PROFILE-01 only: the earlier session flag is no longer recognized, so no invocation can reach an earlier
/// authorization.
/// </summary>
public static class QualificationOptIn
{
    public const string Flag = "--run-qualification-profile-01-exchange";
    public const string ConfirmationVariable = "DEVALCOPILOT_MANUAL_QUALIFICATION_CONFIRM";
    public const string ConfirmationPhrase = "qualification-profile-01-one-codex-planner-and-one-claude-critical-review";

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
