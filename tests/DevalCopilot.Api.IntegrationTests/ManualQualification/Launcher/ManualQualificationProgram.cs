namespace DevalCopilot.Api.IntegrationTests.ManualQualification;

/// <summary>
/// The explicit, manual entry point of the real-provider qualification: one disposable session that asks the installed Codex
/// Planner for one Proposal and the installed Claude CriticalReviewer for one Acceptance or Challenge replying to it. It is never
/// part of a test run. Without the opt-in flag and confirmation phrase it does nothing: no host, no provider, no directory.
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

        return await LauncherEntry.RunAsync(
            args,
            Environment.GetEnvironmentVariable,
            InvocationLedger.CreateDefault(),
            QualificationLimits.Production,
            () => new ProductionQualificationEnvironment(
                QualificationLimits.Production,
                Path.GetTempPath(),
                (root, launcherDirectory) => facts => new LaunchTargetJudge(root, launcherDirectory).Judge(facts)),
            Console.Out,
            Console.Error,
            cancellation.Token);
    }
}
