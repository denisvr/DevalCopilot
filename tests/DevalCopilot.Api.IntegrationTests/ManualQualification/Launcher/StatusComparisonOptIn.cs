using System.Text.RegularExpressions;

namespace DevalCopilot.Api.IntegrationTests.ManualQualification;

/// <summary>
/// The deliberate opt-in of lot A, and its exact typed arguments: the flag, the one Claude executable the owner approved (an absolute
/// path) and that file's SHA-256, in this order, nothing else, together with the exact confirmation phrase in the environment. No
/// search, discovery, default or fallback selects a binary, and neither the flag nor the phrase can start lot B or the earlier session.
/// </summary>
public static partial class StatusComparisonOptIn
{
    public const string Flag = "--run-claude-auth-status-comparison";
    public const string ExecutableOption = "--claude-executable";
    public const string Sha256Option = "--claude-sha256";
    public const string ConfirmationPhrase = "qualification-profile-01-two-claude-auth-status-observations";

    /// <summary>The parsed identity when the arguments and the confirmation are exactly the opt-in, otherwise null.</summary>
    public static (string ExecutablePath, string Sha256)? TryParse(
        IReadOnlyList<string> arguments,
        Func<string, string?> readEnvironment)
    {
        if (arguments.Count != 5
            || arguments[0] != Flag
            || arguments[1] != ExecutableOption
            || arguments[3] != Sha256Option
            || !Digest().IsMatch(arguments[4])
            || !string.Equals(
                readEnvironment(QualificationOptIn.ConfirmationVariable), ConfirmationPhrase, StringComparison.Ordinal))
        {
            return null;
        }

        return (arguments[2], arguments[4].ToLowerInvariant());
    }

    [GeneratedRegex("^[0-9A-Fa-f]{64}$")]
    private static partial Regex Digest();
}
