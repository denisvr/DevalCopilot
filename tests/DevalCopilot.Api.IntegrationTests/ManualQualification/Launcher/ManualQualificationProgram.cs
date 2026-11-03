using System.Collections;
using DevalCopilot.Infrastructure.Features.Processes;

namespace DevalCopilot.Api.IntegrationTests.ManualQualification;

/// <summary>
/// The explicit, manual entry point of QUALIFICATION-PROFILE-01. Exactly two opt-ins exist, each with its own flag, confirmation
/// phrase, authorization and allowance directory: lot A (two <c>claude auth status</c> observations of the owner-approved binary) and
/// lot B (one Codex Planner and one Claude CriticalReviewer exchange through the production host). It is never part of a test run.
/// Without an exact opt-in it does nothing: no host, no provider, no directory.
/// </summary>
public static class ManualQualificationProgram
{
    public static async Task<int> Main(string[] args)
    {
        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };

        if (args.Length > 0 && args[0] == StatusComparisonOptIn.Flag)
        {
            return await StatusComparisonEntry.RunAsync(
                args,
                Environment.GetEnvironmentVariable,
                OrdinaryEnvironment(),
                ProductionClaudeEnvironment.TryBuild,
                Path.Combine(Path.GetTempPath(), StatusComparisonEntry.LedgerDirectoryName),
                Path.GetTempPath(),
                StatusLimits.Production,
                () => new ChildProcessExecutionAdapter(),
                Console.Out,
                Console.Error,
                cancellation.Token);
        }

        var ledger = InvocationLedger.CreateDefault();
        return await LauncherEntry.RunAsync(
            args,
            Environment.GetEnvironmentVariable,
            ledger,
            QualificationLimits.Production,
            deadline => CreateProductionEnvironment(args, ledger, deadline),
            Console.Out,
            Console.Error,
            cancellation.Token);
    }

    /// <summary>Composes lot B only from the owner's approved-launch manifest: its absence, a structural problem or any listed file
    /// that is not exactly the approved one refuses the entry before a host, a directory, a ledger entry or a process exists.</summary>
    private static ProductionQualificationEnvironment CreateProductionEnvironment(
        string[] args,
        InvocationLedger ledger,
        EntryDeadline deadline)
    {
        var manifest = QualificationOptIn.ApprovedLaunchesPath(args)
            ?? throw new EnvironmentRefusedException("ApprovedLaunchesRequired");
        var (approved, refusal) = ApprovedLaunchManifest.Load(manifest);
        if (approved is null)
        {
            throw new EnvironmentRefusedException(refusal ?? "ApprovedLaunchesInvalid");
        }

        return Profile01Composition.CreateEnvironment(
            ledger,
            QualificationLimits.Production,
            Path.GetTempPath(),
            approved,
            (root, launcherDirectory) => facts => new LaunchTargetJudge(root, launcherDirectory).Judge(facts),
            deadline: deadline);
    }

    /// <summary>The launching terminal's own environment, unchanged except for the one confirmation variable the operator set only to
    /// run this entry: the context an ordinary <c>claude</c> invocation would have. Values are handed to the child only and are never
    /// printed or stored.</summary>
    private static Dictionary<string, string> OrdinaryEnvironment() =>
        Environment.GetEnvironmentVariables().Cast<DictionaryEntry>()
            .Where(entry => entry.Key is string name
                && entry.Value is string
                && !string.Equals(name, QualificationOptIn.ConfirmationVariable, StringComparison.OrdinalIgnoreCase))
            .ToDictionary(entry => (string)entry.Key, entry => (string)entry.Value!, StringComparer.OrdinalIgnoreCase);
}
