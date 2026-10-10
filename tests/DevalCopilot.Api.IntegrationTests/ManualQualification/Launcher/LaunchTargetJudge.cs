using System.Text.RegularExpressions;
using DevalCopilot.Api.IntegrationTests.BrowserJourney.ProviderFixture;
using DevalCopilot.Domain.Features.EnvironmentReadiness;

namespace DevalCopilot.Api.IntegrationTests.ManualQualification;

/// <summary>
/// Proves from the host's own readiness evidence that both discovered launch targets are real installed providers and
/// never a fixture: observed by the normal discovery (reason None, a launch kind, a parsed version), a file that exists,
/// outside this launcher's disposable root and output directory, with no fixture executable beside it, and with the exact
/// name or package the catalog expects. It reports only closed codes and a plain version token, never a path, and never
/// invents a version.
/// </summary>
public sealed partial class LaunchTargetJudge(string ownedRoot, string launcherDirectory)
{
    private static readonly ExpectedProvider[] Expected =
    [
        new(Capability.CodexCli, "codex", "codex.exe", Path.Combine("node_modules", "@openai", "codex")),
        new(Capability.ClaudeCli, "claude", "claude.exe", Path.Combine("node_modules", "@anthropic-ai", "claude-code")),
    ];

    public TargetJudgement Judge(IReadOnlyList<ProviderTargetFact> facts)
    {
        var reports = Expected
            .Select(expected => Report(expected, facts.FirstOrDefault(fact => fact.Capability == expected.Capability)))
            .ToArray();
        return new TargetJudgement(reports.All(report => report.Real), reports);
    }

    private TargetReport Report(ExpectedProvider expected, ProviderTargetFact? fact)
    {
        TargetReport Rejected(string code) => new(expected.Provider, false, code, fact?.LaunchKind?.ToString(), null);

        if (fact is null)
        {
            return Rejected("NotObserved");
        }

        if (fact.Reason != CapabilityProbeReason.None)
        {
            return Rejected($"NotObserved{fact.Reason}");
        }

        if (fact.LaunchKind is null || !IsExistingFile(fact.ExecutablePath))
        {
            return Rejected("TargetMissing");
        }

        if (fact.Version is null || !VersionToken().IsMatch(fact.Version))
        {
            return Rejected("VersionUnrecognized");
        }

        var paths = new[] { fact.ExecutablePath!, fact.ScriptPath }.OfType<string>().ToArray();
        var insideFixture = paths.Any(path =>
            OwnedLocation.IsInside(ownedRoot, path) || OwnedLocation.IsInside(launcherDirectory, path));
        if (insideFixture)
        {
            return Rejected("FixtureLocation");
        }

        if (paths.Any(HasFixtureBinaryBeside))
        {
            return Rejected("FixtureBinary");
        }

        return HasExpectedShape(expected, fact)
            ? new TargetReport(expected.Provider, true, "Real", fact.LaunchKind.ToString(), fact.Version)
            : Rejected("UnexpectedLaunchShape");
    }

    private static bool HasExpectedShape(ExpectedProvider expected, ProviderTargetFact fact)
    {
        if (fact.LaunchKind == CapabilityLaunchKind.DirectExecutable)
        {
            return fact.ScriptPath is null && IsNamed(fact.ExecutablePath!, expected.Executable);
        }

        return IsNamed(fact.ExecutablePath!, "node.exe")
            && IsExistingFile(fact.ScriptPath)
            && InsidePackage(fact.ScriptPath!, expected.PackageSegment);
    }

    private static bool IsExistingFile(string? path) =>
        path is not null && Path.IsPathFullyQualified(path) && File.Exists(path);

    private static bool IsNamed(string path, string name) =>
        string.Equals(Path.GetFileName(path), name, StringComparison.OrdinalIgnoreCase);

    private static bool InsidePackage(string scriptPath, string packageSegment)
    {
        var separator = Path.DirectorySeparatorChar;
        return Path.GetFullPath(scriptPath)
            .Contains(separator + packageSegment + separator, StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasFixtureBinaryBeside(string path)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        return directory is not null && Directory.EnumerateFiles(directory, "ProviderFixture*").Any();
    }

    [GeneratedRegex("^[0-9A-Za-z][0-9A-Za-z.+-]{0,63}$")]
    private static partial Regex VersionToken();

    private sealed record ExpectedProvider(
        Capability Capability,
        string Provider,
        string Executable,
        string PackageSegment);
}
