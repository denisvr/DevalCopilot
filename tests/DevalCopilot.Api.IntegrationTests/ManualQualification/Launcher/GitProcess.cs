using System.Diagnostics;
using System.Text;

namespace DevalCopilot.Api.IntegrationTests.ManualQualification;

/// <summary>The only way this launcher runs Git: a fixed argument list, no shell, no optional index locks, bounded by a timeout, and
/// a failure never carries the command's output. Used for the disposable fixture and for read-only snapshots. The child starts from an
/// empty environment: no ambient Git variable, helper, hook path, askpass program or search path can add behavior outside the counted
/// procedure. Only the few variables below are set, and the effective configuration is pinned by the Git configuration-count
/// variables (global and system files ignored, hooks and filesystem monitor disabled, no protocol allowed), which add no argument to
/// any counted command.</summary>
public static class GitProcess
{
    /// <summary>The names of every environment variable a launcher Git child receives, as the admission boundary sees them.</summary>
    public static readonly IReadOnlyList<string> EnvironmentNames =
    [
        "SystemRoot", "GIT_OPTIONAL_LOCKS", "GIT_TERMINAL_PROMPT", "GIT_CONFIG_NOSYSTEM", "GIT_CONFIG_GLOBAL",
        "GIT_CONFIG_COUNT", "GIT_CONFIG_KEY_0", "GIT_CONFIG_VALUE_0", "GIT_CONFIG_KEY_1", "GIT_CONFIG_VALUE_1",
        "GIT_CONFIG_KEY_2", "GIT_CONFIG_VALUE_2", "GIT_CONFIG_KEY_3", "GIT_CONFIG_VALUE_3",
    ];

    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(60);

    private static readonly (string Key, string Value)[] PinnedConfiguration =
    [
        ("core.hooksPath", "NUL"), ("core.fsmonitor", "false"), ("core.untrackedCache", "false"), ("protocol.allow", "never"),
    ];

    public static string Run(string workingDirectory, params string[] arguments) =>
        Run(null, workingDirectory, arguments);

    /// <summary>The same fixed run, admitted first when a qualification session supplies its admission boundary: an unlisted,
    /// drifted or exhausted command throws its closed refusal before any process exists. Under an admission the executable is the
    /// owner-approved absolute Git file (the very identity the host uses), never a name resolved through PATH.</summary>
    public static string Run(ProcessAdmission? admission, string workingDirectory, params string[] arguments)
    {
        var executable = admission is null ? "git" : admission.LauncherGitPath ?? "git";
        var environment = ChildEnvironment();
        var admitted = admission?.Admit(new ProcessShape(executable, arguments)
        {
            WorkingDirectory = workingDirectory,
            Timeout = Bound,
            EnvironmentNames = [.. environment.Keys.OrderBy(name => name, StringComparer.OrdinalIgnoreCase)],
        });
        if (admitted is not null)
        {
            admission!.ConfirmOpen(admitted);
        }

        var start = new ProcessStartInfo(executable)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
        };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        start.Environment.Clear();
        foreach (var (name, value) in environment)
        {
            start.Environment[name] = value;
        }

        using var process = Process.Start(start) ?? throw new InvalidOperationException("Git could not be started.");
        var error = process.StandardError.ReadToEndAsync();
        var output = process.StandardOutput.ReadToEndAsync();
        if (!process.WaitForExit(Bound))
        {
            process.Kill(entireProcessTree: true);
            throw new InvalidOperationException("Git did not finish within its bound.");
        }

        process.WaitForExit();
        _ = error.Result;
        return process.ExitCode == 0
            ? output.Result
            : throw new InvalidOperationException($"Git exited with code {process.ExitCode}.");
    }

    /// <summary>The complete environment of a launcher Git child. Values are never printed or kept.</summary>
    public static IReadOnlyDictionary<string, string> ChildEnvironment()
    {
        var environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["GIT_OPTIONAL_LOCKS"] = "0",
            ["GIT_TERMINAL_PROMPT"] = "0",
            ["GIT_CONFIG_NOSYSTEM"] = "1",
            ["GIT_CONFIG_GLOBAL"] = "NUL",
            ["GIT_CONFIG_COUNT"] = PinnedConfiguration.Length.ToString(System.Globalization.CultureInfo.InvariantCulture),
        };
        for (var index = 0; index < PinnedConfiguration.Length; index++)
        {
            environment[$"GIT_CONFIG_KEY_{index}"] = PinnedConfiguration[index].Key;
            environment[$"GIT_CONFIG_VALUE_{index}"] = PinnedConfiguration[index].Value;
        }

        if (Environment.GetEnvironmentVariable("SystemRoot") is { Length: > 0 } systemRoot)
        {
            environment["SystemRoot"] = systemRoot;
        }

        return environment;
    }
}
