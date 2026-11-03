using System.Security.Cryptography;
using System.Text;
using DevalCopilot.Api.IntegrationTests.BrowserJourney.Host;
using DevalCopilot.Application.Features.Processes.Ports;

namespace DevalCopilot.Api.IntegrationTests.ManualQualification;

/// <summary>
/// Lot A behind its entry point, with every outside effect passed in so the offline regressions can prove each refusal, bound and
/// stop with owned doubles. After the exact opt-in and the binary's identity check it consumes nothing but its own durable process
/// slots (one per context, before each start), runs the two serial <c>claude auth status</c> observations through the shared process
/// boundary in one new disposable working directory, prints and keeps only the closed summary, and removes only its own root when
/// both children demonstrably finished. It never starts a model, version, help or account command, and a second run of the same
/// authorization is refused before any process, directory or slot exists. One monotonic <see cref="EntryDeadline"/>, started as the
/// first statement, bounds everything: the binary verification, the filtered-map preparation, the observations, the closure and the
/// report. The waiter never runs the work it waits for, so a blocking preparation or a held sink cannot keep the entry from
/// returning at the bound; after it nothing can start, clean or rewrite the report.
/// </summary>
public static class StatusComparisonEntry
{
    public const string LedgerDirectoryName = "devalcopilot-qualification-profile-01-lot-a-ledger";
    public const int RefusedExitCode = 2;
    public const int SpentExitCode = 3;
    public const int NotAuthenticatedExitCode = 5;
    public const int UnexpectedFailureExitCode = 7;
    public const int OuterDeadlineExitCode = 8;

    public static async Task<int> RunAsync(
        IReadOnlyList<string> arguments,
        Func<string, string?> readVariable,
        IReadOnlyDictionary<string, string> ordinaryEnvironment,
        Func<Dictionary<string, string>?> buildFilteredEnvironment,
        string ledgerDirectory,
        string temporaryDirectory,
        StatusLimits limits,
        Func<IProcessExecutionAdapter> createAdapter,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        var deadline = EntryDeadline.Start(limits.Outer);
        if (StatusComparisonOptIn.TryParse(arguments, readVariable) is not { } identity)
        {
            await deadline.EmitAsync(() => error.WriteLineAsync(
                $"Refused: this entry runs only with exactly {StatusComparisonOptIn.Flag} {StatusComparisonOptIn.ExecutableOption} "
                + $"<absolute path> {StatusComparisonOptIn.Sha256Option} <digest> and {QualificationOptIn.ConfirmationVariable} set to "
                + "the documented confirmation phrase."));
            return RefusedExitCode;
        }

        var sessionId = Guid.NewGuid().ToString("N");
        var progress = new Progress();
        var preparing = Task.Run(() => PrepareAndObserveAsync(
            identity, sessionId, progress, deadline, ordinaryEnvironment, buildFilteredEnvironment, ledgerDirectory,
            temporaryDirectory, limits, createAdapter, cancellationToken));
        try
        {
            if (!await deadline.WithinAsync(preparing))
            {
                return await ExpireAsync(deadline, sessionId, progress, ledgerDirectory, output, error, reportInFlight: false);
            }

            var outcome = await preparing;
            if (outcome.RefusalText is not null)
            {
                await deadline.EmitAsync(() => error.WriteLineAsync(outcome.RefusalText));
                return outcome.ExitCode;
            }

            if (!deadline.TryClaimReport())
            {
                return await ExpireAsync(deadline, sessionId, progress, ledgerDirectory, output, error, reportInFlight: false);
            }

            // Print, then commit, then persist: a summary is never persisted after the deadline decided otherwise, and a sink call
            // that is still running at the bound leaves its bytes non-final and unproven.
            // The persistence outcome is checked: a summary that could not be created exclusively, written, flushed and closed turns a
            // successful exit into the closed not-persisted attention exit (a more specific non-success exit is kept), and a
            // finalization that did not commit never returns success.
            var closing = Task.Run(async () =>
            {
                await output.WriteLineAsync(outcome.Summary);
                if (!deadline.TryCommit())
                {
                    return (ExitCode: outcome.ExitCode, Committed: false);
                }

                var persisted = await SummaryPersistence.SaveAsync(ledgerDirectory, $"status-summary-{sessionId}.json", outcome.Summary!);
                if (!persisted)
                {
                    await deadline.EmitAsync(() => error.WriteLineAsync(
                        "The summary could not be persisted beside the ledger; the printed result stands but is not kept."));
                }

                return (ExitCode: SummaryPersistence.ExitCode(outcome.ExitCode, persisted), Committed: true);
            });
            if (await deadline.WithinAsync(closing))
            {
                var finished = await closing;
                return finished.Committed
                    ? finished.ExitCode
                    : await ExpireAsync(deadline, sessionId, progress, ledgerDirectory, output, error, reportInFlight: true);
            }

            if (deadline.TryClaimExpiry(out _))
            {
                return await ExpireAsync(deadline, sessionId, progress, ledgerDirectory, output, error, reportInFlight: true);
            }

            if (await EntryDeadline.WithinAsync(closing, deadline.HardRemaining))
            {
                return (await closing).ExitCode;
            }

            await deadline.EmitAsync(() => error.WriteLineAsync(
                "The entry's bound elapsed while its committed report was being saved; the save is unproven."));
            return OuterDeadlineExitCode;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // A runtime stack trace would print local paths: only the exception type is reported.
            await deadline.EmitAsync(() => error.WriteLineAsync($"The entry stopped unexpectedly ({exception.GetType().Name})."));
            return UnexpectedFailureExitCode;
        }
    }

    /// <summary>Everything between the opt-in and the report that can block or take time: the binary verification, the spent check,
    /// the root, the filtered-map preparation, the observations and the closure of the root. Each effect first asks the deadline.</summary>
    private static async Task<Outcome> PrepareAndObserveAsync(
        (string ExecutablePath, string Sha256) identity,
        string sessionId,
        Progress progress,
        EntryDeadline deadline,
        IReadOnlyDictionary<string, string> ordinaryEnvironment,
        Func<Dictionary<string, string>?> buildFilteredEnvironment,
        string ledgerDirectory,
        string temporaryDirectory,
        StatusLimits limits,
        Func<IProcessExecutionAdapter> createAdapter,
        CancellationToken cancellationToken)
    {
        var (claudePath, pinned) = identity;
        var (facts, refusal) = await ClaudeBinaryVerifier.VerifyAsync(claudePath, pinned, cancellationToken);
        if (facts is null)
        {
            return Refused($"Refused: {refusal}.", RefusedExitCode);
        }

        progress.Binary = facts;
        var counters = new AdmissionLedger(ledgerDirectory, ClaudeStatusSummary.Authorization, TimeProvider.System);
        if (counters.AnyConsumed())
        {
            return Refused("Refused: this authorization's process slots are already spent; no rearm exists.", SpentExitCode);
        }

        if (deadline.IsExpired)
        {
            return Expired();
        }

        var token = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        var context = new StatusContext();
        var approved = new ApprovedFile(claudePath, pinned);
        var admission = new ProcessAdmission(
            StatusInventory.Create(claudePath, context, limits.Execution),
            StatusInventory.Total,
            counters,
            _ => approved.Verify(),
            () => deadline.IsExpired ? "OuterDeadlineExceeded" : null);
        progress.Admission = admission;
        var runner = new ClaudeStatusRunner(new AdmittingProcessExecutionAdapter(createAdapter(), admission), context);
        if (deadline.IsExpired)
        {
            return Expired();
        }

        var root = CreateRoot(temporaryDirectory, token);
        context.WorkingDirectory = root;
        var filtered = buildFilteredEnvironment();
        var run = await runner.RunAsync(
            claudePath, root, ordinaryEnvironment, filtered, limits.Execution, limits.Sequence, cancellationToken);

        // After the bound nothing is cleaned, stopped or rewritten from this continuation: the root stays as it is, unproven.
        var cleanup = deadline.IsExpired
            ? CleanupReport.Preserved("OuterDeadlineExceeded")
            : AllChildrenFinished(run) ? OwnedRootCleaner.Remove(root, token) : CleanupReport.Preserved("ObservationNotComplete");
        var summary = ClaudeStatusSummary.Render(
            sessionId,
            run,
            facts,
            ClaudeStatusEnvironmentFacts.Describe(ordinaryEnvironment),
            ClaudeStatusEnvironmentFacts.Describe(filtered),
            admission.Facts,
            cleanup,
            [claudePath, root, Path.GetFullPath(temporaryDirectory), Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)]);
        return new Outcome(summary, null, run.Decision == ClaudeStatusDecision.BothAuthenticated && cleanup.Removed ? 0 : NotAuthenticatedExitCode);
    }

    private static Outcome Refused(string text, int exitCode) => new(null, text, exitCode);

    private static Outcome Expired() => new(null, null, OuterDeadlineExitCode);

    /// <summary>Every child that was started finished physically with complete I/O: only then is its directory removed.</summary>
    private static bool AllChildrenFinished(ClaudeStatusRun run) =>
        Finished(run.Ordinary) && Finished(run.Filtered);

    private static bool Finished(ClaudeStatusObservation observation) =>
        observation.Physical is ClaudeStatusPhysical.NotStarted or ClaudeStatusPhysical.Refused
        || (observation.Physical == ClaudeStatusPhysical.Exited && observation.IoComplete);

    private static string CreateRoot(string temporaryDirectory, string token)
    {
        var path = Path.Combine(temporaryDirectory, OwnedRootGuard.RootPrefix + "status-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        using (var marker = new FileStream(Path.Combine(path, OwnedRootGuard.MarkerFile), FileMode.CreateNew, FileAccess.Write))
        {
            marker.Write(Encoding.ASCII.GetBytes(token));
        }

        return OwnedRootGuard.Verify(path, token).Root;
    }

    /// <summary>The fixed unproven verdict of an entry whose bound elapsed. A start whose slot was consumed is reported as started but
    /// unproven, never as not started; nothing is stopped or removed from here.</summary>
    private static async Task<int> ExpireAsync(
        EntryDeadline deadline,
        string sessionId,
        Progress progress,
        string ledgerDirectory,
        TextWriter output,
        TextWriter error,
        bool reportInFlight)
    {
        deadline.TryClaimExpiry(out _);
        var admission = progress.Admission?.Facts ?? ProcessAdmissionFacts.None;
        var unproven = new ClaudeStatusRun(
            Unproven(ClaudeStatusObservation.Ordinary, "claude.status.ordinary", admission),
            Unproven(ClaudeStatusObservation.Filtered, "claude.status.filtered", admission),
            ClaudeStatusDecision.Stopped,
            "OuterDeadlineExceeded",
            ConfigDirectoryEquality.Unknown);
        var expired = ClaudeStatusSummary.Render(
            sessionId,
            unproven,
            progress.Binary ?? new ClaudeBinaryFacts(false, 0, "unverified"),
            ClaudeStatusEnvironmentFacts.Describe(null),
            ClaudeStatusEnvironmentFacts.Describe(null),
            admission,
            CleanupReport.Preserved("OuterDeadlineExceeded"),
            []);
        var emissions = new List<Task<bool>> { deadline.EmitAsync(() => SummaryPersistence.SaveAsync(ledgerDirectory, $"status-summary-{sessionId}.json", expired)) };
        emissions.Add(
            reportInFlight
                ? deadline.EmitAsync(() => error.WriteLineAsync(
                    "The entry's bound elapsed while its report was still running; the report is unproven."))
                : deadline.EmitAsync(() => output.WriteLineAsync(expired)));
        await Task.WhenAll(emissions);
        return OuterDeadlineExitCode;
    }

    /// <summary>A start whose slot was consumed or is being consumed (pending) is started but unproven, never not started.</summary>
    private static ClaudeStatusObservation Unproven(string context, string slot, ProcessAdmissionFacts admission) =>
        ClaudeStatusObservation.Unobserved(
            context,
            admission.Admitted.ContainsKey(slot) || admission.PendingClasses.Contains(slot)
                ? ClaudeStatusPhysical.StartedUnproven
                : ClaudeStatusPhysical.NotStarted);

    /// <summary>What the waiter may read of the work in flight: the verified binary and the admission boundary, published the moment
    /// they exist. Read only to render the unproven verdict.</summary>
    private sealed class Progress
    {
        private volatile ClaudeBinaryFacts? _binary;
        private volatile ProcessAdmission? _admission;

        public ClaudeBinaryFacts? Binary
        {
            get => _binary;
            set => _binary = value;
        }

        public ProcessAdmission? Admission
        {
            get => _admission;
            set => _admission = value;
        }
    }

    private sealed record Outcome(string? Summary, string? RefusalText, int ExitCode);
}
