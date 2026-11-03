namespace DevalCopilot.Api.IntegrationTests.ManualQualification;

/// <summary>The launcher's whole flow behind its entry point, with every outside effect passed in so the offline regressions can
/// prove that a refused launch creates no environment, host, directory or ledger entry. One monotonic <see cref="EntryDeadline"/>,
/// started as the first statement, bounds everything after it, the reporting of refusals, errors and expiry included: the stated
/// outer budget is never exceeded by a post-budget wait. The work runs on its own task, so a blocking preparation or a held sink
/// cannot keep the entry from returning.
/// The order of a normal end is: the session finishes, the entry claims its report, the environment is closed (disposed), the summary
/// (which states that closure) is printed, the entry commits, and only then is the summary persisted. Authoritative success is
/// therefore never printed or kept before the required closure, and a summary is never persisted after the deadline decided
/// otherwise. A sink call already in progress when the deadline elapses cannot be revoked: its bytes are non-final and unproven, the
/// entry returns exit 8 with a fixed statement, and the detached continuation initiates no new effect.</summary>
public static class LauncherEntry
{
    public const int RefusedExitCode = 2;
    public const int UnexpectedFailureExitCode = 7;
    public const int OuterDeadlineExitCode = 8;

    public static Task<int> RunAsync(
        IReadOnlyList<string> arguments,
        Func<string, string?> readEnvironment,
        InvocationLedger ledger,
        QualificationLimits limits,
        Func<IQualificationEnvironment> createEnvironment,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken) =>
        RunAsync(arguments, readEnvironment, ledger, limits, _ => createEnvironment(), output, error, cancellationToken);

    public static async Task<int> RunAsync(
        IReadOnlyList<string> arguments,
        Func<string, string?> readEnvironment,
        InvocationLedger ledger,
        QualificationLimits limits,
        Func<EntryDeadline, IQualificationEnvironment> createEnvironment,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        var deadline = limits.Outer is { } outer ? EntryDeadline.Start(outer) : EntryDeadline.Unbounded();
        if (!QualificationOptIn.IsGranted(arguments, readEnvironment))
        {
            await deadline.EmitAsync(() => error.WriteLineAsync(
                $"Refused: this launcher runs only with the argument {QualificationOptIn.Flag} and "
                + $"{QualificationOptIn.ConfirmationVariable} set to the documented confirmation phrase."));
            return RefusedExitCode;
        }

        var sessionId = Guid.NewGuid().ToString("N");
        IQualificationEnvironment? environment = null;
        var preparing = Task.Run(async () =>
        {
            var gated = new DeadlineGatedEnvironment(createEnvironment(deadline), deadline);
            Volatile.Write(ref environment, gated);
            var report = await new QualificationSession(ledger, gated, limits, sessionId, deadline).RunAsync(cancellationToken);
            return report;
        });

        try
        {
            if (!await deadline.WithinAsync(preparing))
            {
                return await ExpireAsync(deadline, sessionId, Volatile.Read(ref environment), ledger, output, error, "Session");
            }

            var report = await preparing;
            if (!deadline.TryClaimReport())
            {
                return await ExpireAsync(deadline, sessionId, Volatile.Read(ref environment), ledger, output, error, "Session");
            }

            var closing = Task.Run(() => CloseAndReportAsync(deadline, report, Volatile.Read(ref environment)!, ledger, sessionId, output, error));
            if (await deadline.WithinAsync(closing))
            {
                // Only a committed finalization may return its own exit code; an uncommitted one (the deadline decided first) is
                // the unproven verdict, never success.
                var finished = await closing;
                return finished.Committed
                    ? finished.ExitCode
                    : await ExpireAsync(deadline, sessionId, Volatile.Read(ref environment), ledger, output, error, "ReportOrClosure");
            }

            // The deadline elapsed while the closure or the report was still running.
            return deadline.TryClaimExpiry(out _)
                ? await ExpireAsync(deadline, sessionId, Volatile.Read(ref environment), ledger, output, error, "ReportOrClosure")
                : await FinishCommittedAsync(deadline, closing, error);
        }
        catch (EnvironmentRefusedException refused)
        {
            await deadline.EmitAsync(() => error.WriteLineAsync($"Refused: {refused.Code}."));
            return RefusedExitCode;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // A runtime stack trace would print local paths: only the exception type is reported.
            await deadline.EmitAsync(() => error.WriteLineAsync($"The launcher stopped unexpectedly ({exception.GetType().Name})."));
            await DisposeAfterFailureAsync(deadline, Volatile.Read(ref environment));
            return UnexpectedFailureExitCode;
        }
    }

    /// <summary>The outcome of a finalization: the exit code it would return and whether it committed. Only a committed finalization
    /// has persisted (or definitively failed to persist) its summary, so only a committed one may return its exit code.</summary>
    private sealed record Finalization(int ExitCode, bool Committed);

    /// <summary>The ordered end of a session that finished in time: close the environment, render and print the summary (which
    /// states the closure), commit, then persist, checked: a summary that could not be created exclusively, written, flushed and closed
    /// turns a successful or attention-only exit into the closed not-persisted attention exit, with the printed facts, the spent
    /// allowances and every existing file left exactly as they are (no retry, refund or overwrite). Any step that finds the deadline
    /// gone stops without a new effect and returns an uncommitted finalization.</summary>
    private static async Task<Finalization> CloseAndReportAsync(
        EntryDeadline deadline,
        QualificationReport report,
        IQualificationEnvironment environment,
        InvocationLedger ledger,
        string sessionId,
        TextWriter output,
        TextWriter error)
    {
        try
        {
            await environment.DisposeAsync();
            report.EnvironmentDisposed = true;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            report.EnvironmentDisposed = false;
            report.Disqualify("EnvironmentDisposalFailed");
        }

        if (deadline.IsExpired)
        {
            return new Finalization(report.ExitCode, false);
        }

        var summary = SafeSummary.Render(report, environment.ForbiddenFragments);
        await output.WriteLineAsync(summary);
        if (!deadline.TryCommit())
        {
            return new Finalization(report.ExitCode, false);
        }

        var persisted = await SummaryPersistence.SaveAsync(ledger.LedgerDirectory, $"summary-{sessionId}.json", summary);
        if (!persisted)
        {
            await deadline.EmitAsync(() => error.WriteLineAsync(
                "The summary could not be persisted beside the ledger; the printed result stands but is not kept."));
        }

        return new Finalization(SummaryPersistence.ExitCode(report.ExitCode, persisted), true);
    }

    /// <summary>The report was committed just before the deadline: only its quick local save remains, awaited for at most the time left
    /// in the budget. The exit code is the committed finalization's if it finished, otherwise exit 8 with the fixed statement.</summary>
    private static async Task<int> FinishCommittedAsync(EntryDeadline deadline, Task<Finalization> closing, TextWriter error)
    {
        if (await EntryDeadline.WithinAsync(closing, deadline.HardRemaining))
        {
            return (await closing).ExitCode;
        }

        await deadline.EmitAsync(() => error.WriteLineAsync(
            "The entry's bound elapsed while its committed report was being saved; the save is unproven."));
        return OuterDeadlineExitCode;
    }

    /// <summary>The fixed unproven verdict of an entry whose bound elapsed: only the identity and the closed admission progress
    /// (read from a snapshot that never waits for work in flight). Nothing is stopped, removed or cleaned from here, and nothing the
    /// detached continuation does afterwards can start a process, consume an allowance or rewrite this. If the report was already
    /// in flight its sink is in use and is left alone: the verdict is persisted and the fixed statement goes to the error stream.
    /// Everything here is bounded by the time left in the stated budget.</summary>
    private static async Task<int> ExpireAsync(
        EntryDeadline deadline,
        string sessionId,
        IQualificationEnvironment? environment,
        InvocationLedger ledger,
        TextWriter output,
        TextWriter error,
        string expiredDuring)
    {
        deadline.TryClaimExpiry(out _);
        var expired = SafeSummary.RenderOuterDeadline(
            sessionId, environment?.AdmissionFacts ?? ProcessAdmissionFacts.None, expiredDuring);
        var emissions = new List<Task<bool>> { deadline.EmitAsync(() => SummaryPersistence.SaveAsync(ledger.LedgerDirectory, $"summary-{sessionId}.json", expired)) };
        if (expiredDuring == "Session")
        {
            emissions.Add(deadline.EmitAsync(() => output.WriteLineAsync(expired)));
        }
        else
        {
            emissions.Add(deadline.EmitAsync(() => error.WriteLineAsync(
                "The entry's bound elapsed while its report or closure was still running; both are unproven.")));
        }

        await Task.WhenAll(emissions);
        return OuterDeadlineExitCode;
    }

    private static async Task DisposeAfterFailureAsync(EntryDeadline deadline, IQualificationEnvironment? environment)
    {
        if (environment is null || deadline.IsExpired)
        {
            return;
        }

        try
        {
            await environment.DisposeAsync().AsTask().WaitAsync(deadline.Remaining);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // The failure is already reported; a closure that cannot finish inside the bound stays unproven.
        }
    }
}
