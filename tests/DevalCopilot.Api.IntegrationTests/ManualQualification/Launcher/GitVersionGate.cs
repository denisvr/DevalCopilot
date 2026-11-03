using System.Text.RegularExpressions;

namespace DevalCopilot.Api.IntegrationTests.ManualQualification;

/// <summary>
/// The syntax prerequisite of the launcher's constrained Git configuration, observed before its first fixture mutation: one bounded
/// <c>--version</c> request of the exact approved Git file, admitted as the already budgeted <c>discovery.git</c> class (so it counts
/// against that class' eight starts and the 179 maximum), whose coherent version must be at least <see cref="Minimum"/> (the first
/// Git that honors <c>GIT_CONFIG_GLOBAL</c>). Unknown, older, failed, oversized, incomplete or unproven-termination observations refuse
/// with a closed code;
/// nothing falls back, retries, or ignores a flag. The minimum establishes syntax support only, not universal compatibility or
/// security qualification, and a digest identifies a file without proving any flag support.
/// </summary>
public static partial class GitVersionGate
{
    public static readonly Version Minimum = new(2, 32, 0);

    public static readonly TimeSpan Bound = TimeSpan.FromSeconds(5);

    /// <summary>The most bytes either stream of the observation may carry; the observer enforces it on both streams.</summary>
    public const int MaximumOutputBytes = 512;

    /// <summary>Admits and observes the approved Git once and throws the closed refusal unless it is coherent and new enough.</summary>
    public static void Require(ProcessAdmission admission, IGitVersionObserver observer, string scratchDirectory)
    {
        var git = admission.LauncherGitPath ?? throw new GitCompatibilityException("GitVersionNotApproved");
        Directory.CreateDirectory(scratchDirectory);
        var admitted = admission.Admit(new ProcessShape(git, ["--version"])
        {
            WorkingDirectory = scratchDirectory,
            ApprovedRoot = scratchDirectory,
            Timeout = Bound,
            EnvironmentNames = [.. observer.EnvironmentNames],
        });
        admission.ConfirmOpen(admitted);
        if (Evaluate(observer.Observe(git, scratchDirectory, Bound)) is { } code)
        {
            throw new GitCompatibilityException(code);
        }
    }

    /// <summary>The closed refusal code of an observation, or null when it is one coherent version of at least the minimum.</summary>
    public static string? Evaluate(GitVersionObservation observation)
    {
        if (observation.TerminationUnproven)
        {
            // Cleanup could not prove that the process, or a descendant holding a pipe, ended: reported as such, whatever it printed.
            return "GitVersionTerminationUnproven";
        }

        if (observation.Truncated || observation.Output.Length > MaximumOutputBytes)
        {
            return "GitVersionIncomplete";
        }

        if (observation.TimedOut || !observation.Exited || observation.ExitCode != 0)
        {
            return "GitVersionObservationFailed";
        }

        if (!observation.IoComplete)
        {
            return "GitVersionIncomplete";
        }

        var match = VersionLine().Match(observation.Output);
        if (!match.Success
            || !int.TryParse(match.Groups["major"].Value, out var major)
            || !int.TryParse(match.Groups["minor"].Value, out var minor)
            || !int.TryParse(match.Groups["patch"].Value, out var patch))
        {
            return "GitVersionUnknown";
        }

        return new Version(major, minor, patch) >= Minimum ? null : "GitVersionTooOld";
    }

    [GeneratedRegex("^git version (?<major>[0-9]{1,4})\\.(?<minor>[0-9]{1,4})\\.(?<patch>[0-9]{1,4})(?:\\.[0-9A-Za-z.+-]{1,64})?\\r?\\n?\\z")]
    private static partial Regex VersionLine();
}
