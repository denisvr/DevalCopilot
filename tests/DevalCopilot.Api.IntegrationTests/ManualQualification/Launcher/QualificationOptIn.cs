namespace DevalCopilot.Api.IntegrationTests.ManualQualification;

/// <summary>
/// The deliberate opt-in without which nothing starts: no host, no provider, no directory and no ledger entry. It needs the one
/// fixed flag as the only argument and the exact confirmation phrase in the environment, so neither an ambient variable nor an
/// accidental invocation can enable a real-provider session.
/// </summary>
public static class QualificationOptIn
{
    public const string Flag = "--run-real-provider-session";
    public const string ConfirmationVariable = "DEVALCOPILOT_MANUAL_QUALIFICATION_CONFIRM";
    public const string ConfirmationPhrase = "one-codex-planner-and-one-claude-critical-review";

    public static bool IsGranted(IReadOnlyList<string> arguments, Func<string, string?> readEnvironment) =>
        arguments.Count == 1
        && string.Equals(arguments[0], Flag, StringComparison.Ordinal)
        && string.Equals(readEnvironment(ConfirmationVariable), ConfirmationPhrase, StringComparison.Ordinal);
}
