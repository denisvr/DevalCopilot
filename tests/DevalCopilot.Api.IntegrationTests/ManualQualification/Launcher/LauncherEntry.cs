namespace DevalCopilot.Api.IntegrationTests.ManualQualification;

/// <summary>The launcher's whole flow behind its entry point, with every outside effect passed in so the offline regressions can
/// prove that a refused launch creates no environment, host, directory or ledger entry.</summary>
public static class LauncherEntry
{
    public const int RefusedExitCode = 2;
    public const int UnexpectedFailureExitCode = 7;

    public static async Task<int> RunAsync(
        IReadOnlyList<string> arguments,
        Func<string, string?> readEnvironment,
        InvocationLedger ledger,
        QualificationLimits limits,
        Func<IQualificationEnvironment> createEnvironment,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        if (!QualificationOptIn.IsGranted(arguments, readEnvironment))
        {
            await error.WriteLineAsync(
                $"Refused: this launcher runs only with the argument {QualificationOptIn.Flag} and "
                + $"{QualificationOptIn.ConfirmationVariable} set to the documented confirmation phrase.");
            return RefusedExitCode;
        }

        try
        {
            var sessionId = Guid.NewGuid().ToString("N");
            await using var environment = createEnvironment();
            var session = new QualificationSession(ledger, environment, limits, sessionId);
            var report = await session.RunAsync(cancellationToken);

            var summary = SafeSummary.Render(report, environment.ForbiddenFragments);
            await output.WriteLineAsync(summary);
            await SaveSummaryAsync(ledger, sessionId, summary, error);
            return report.ExitCode;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // A runtime stack trace would print local paths: only the exception type is reported.
            await error.WriteLineAsync($"The launcher stopped unexpectedly ({exception.GetType().Name}).");
            return UnexpectedFailureExitCode;
        }
    }

    private static async Task SaveSummaryAsync(
        InvocationLedger ledger,
        string sessionId,
        string summary,
        TextWriter error)
    {
        try
        {
            Directory.CreateDirectory(ledger.LedgerDirectory);
            await File.WriteAllTextAsync(Path.Combine(ledger.LedgerDirectory, $"summary-{sessionId}.json"), summary);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            await error.WriteLineAsync("The summary could not be saved beside the ledger.");
        }
    }
}
