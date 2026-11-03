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

    /// <summary>The closed refusal of a ledger destination that is itself a junction, symbolic link or other reparse point.</summary>
    public const string LedgerAliasedCode = "LedgerDestinationAliased";

    public static Task<int> RunAsync(
        IReadOnlyList<string> arguments,
        Func<string, string?> readEnvironment,
        InvocationLedger ledger,
        QualificationLimits limits,
        Func<IQualificationEnvironment> createEnvironment,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken,
        Func<string, bool>? inspectLedger = null) =>
        RunAsync(arguments, readEnvironment, ledger, limits, _ => createEnvironment(), output, error, cancellationToken, inspectLedger);

    public static async Task<int> RunAsync(
        IReadOnlyList<string> arguments,
        Func<string, string?> readEnvironment,
        InvocationLedger ledger,
        QualificationLimits limits,
        Func<EntryDeadline, IQualificationEnvironment> createEnvironment,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken,
        Func<string, bool>? inspectLedger = null)
    {
        // The only metadata read of the ledger destination: production uses the attribute check; an owned double may hold it. Every
        // read runs inside the bounded worker or an emission, never on the waiter, and is followed by a clock decision.
        var isAliased = inspectLedger ?? InvocationLedger.IsAliasedDestination;
        var deadline = limits.Outer is { } outer ? EntryDeadline.Start(outer) : EntryDeadline.Unbounded();
        if (!QualificationOptIn.IsGranted(arguments, readEnvironment))
        {
            var retired = arguments.Count > 0 && string.Equals(arguments[0], QualificationOptIn.RetiredFlag, StringComparison.Ordinal);
            await deadline.EmitAsync(() => error.WriteLineAsync(retired
                ? $"Refused: {QualificationOptIn.RetiredFlag} is retired; its allowance and process slots were consumed. The "
                    + $"replacement entry is {QualificationOptIn.Flag}."
                : $"Refused: this launcher runs only with the argument {QualificationOptIn.Flag} and "
                    + $"{QualificationOptIn.ConfirmationVariable} set to the documented confirmation phrase."));
            return RefusedExitCode;
        }

        var sessionId = Guid.NewGuid().ToString("N");
        IQualificationEnvironment? environment = null;
        var preparing = Task.Run(async () =>
        {
            var aliased = isAliased(ledger.LedgerDirectory);
            if (deadline.IsExpired)
            {
                throw new OperationCanceledException("The entry deadline elapsed.");
            }

            if (aliased)
            {
                throw new EnvironmentRefusedException(LedgerAliasedCode);
            }

            var gated = new DeadlineGatedEnvironment(createEnvironment(deadline), deadline);
            Volatile.Write(ref environment, gated);
            var report = await new QualificationSession(ledger, gated, limits, sessionId, deadline, isAliased).RunAsync(cancellationToken);
            return report;
        });

        try
        {
            if (!await deadline.WithinAsync(preparing))
            {
                return await ExpireAsync(deadline, sessionId, Volatile.Read(ref environment), ledger, isAliased, output, error, "Session");
            }

            var report = await preparing;
            if (!deadline.TryClaimReport())
            {
                return await ExpireAsync(deadline, sessionId, Volatile.Read(ref environment), ledger, isAliased, output, error, "Session");
            }

            var closing = Task.Run(() => CloseAndReportAsync(
                deadline, report, Volatile.Read(ref environment)!, ledger, isAliased, sessionId, output, error));
            if (await deadline.WithinAsync(closing))
            {
                // Only a committed finalization may return its own exit code; an uncommitted one (the deadline decided first) is
                // the unproven verdict, never success.
                var finished = await closing;
                return finished.Committed
                    ? finished.ExitCode
                    : await ExpireAsync(
                        deadline, sessionId, Volatile.Read(ref environment), ledger, isAliased, output, error, "ReportOrClosure");
            }

            // The deadline elapsed while the closure or the report was still running.
            return deadline.TryClaimExpiry(out _)
                ? await ExpireAsync(
                    deadline, sessionId, Volatile.Read(ref environment), ledger, isAliased, output, error, "ReportOrClosure")
                : await FinishCommittedAsync(deadline, closing, error);
        }
        catch (EnvironmentRefusedException refused)
        {
            await deadline.EmitAsync(() => error.WriteLineAsync($"Refused: {refused.Code}."));
            return RefusedExitCode;
        }
        catch (OperationCanceledException) when (deadline.IsExpired)
        {
            // The preparation stopped at the deadline and won the race against the waiter: the bound decided, not a failure.
            return await ExpireAsync(deadline, sessionId, Volatile.Read(ref environment), ledger, isAliased, output, error, "Session");
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
        Func<string, bool> isAliased,
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

        var persisted = await SaveSummaryAsync(ledger, isAliased, $"summary-{sessionId}.json", summary);
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
        Func<string, bool> isAliased,
        TextWriter output,
        TextWriter error,
        string expiredDuring)
    {
        deadline.TryClaimExpiry(out _);
        var expired = SafeSummary.RenderOuterDeadline(
            sessionId, environment?.AdmissionFacts ?? ProcessAdmissionFacts.None, expiredDuring);
        // The destination is judged inside the emission, never on the waiter: a held metadata read cannot keep the entry past its bound.
        var emissions = new List<Task<bool>> { deadline.EmitAsync(() => SaveSummaryAsync(ledger, isAliased, $"summary-{sessionId}.json", expired)) };
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

    /// <summary>The summary is saved only to a destination that is still not an alias; otherwise nothing is created and the save is
    /// reported as not persisted, which the normal path turns into the attention exit and the expiry path leaves unproven.</summary>
    private static Task<bool> SaveSummaryAsync(InvocationLedger ledger, Func<string, bool> isAliased, string fileName, string text) =>
        isAliased(ledger.LedgerDirectory)
            ? Task.FromResult(false)
            : SummaryPersistence.SaveAsync(ledger.LedgerDirectory, fileName, text);

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
