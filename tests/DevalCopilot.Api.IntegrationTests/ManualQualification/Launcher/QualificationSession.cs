namespace DevalCopilot.Api.IntegrationTests.ManualQualification;

/// <summary>
/// The one acceptance session and every control around its two provider invocations. Setup, readiness and the fictitious
/// fixture come first and spend nothing. Each allowance is durably consumed before its single POST, which is never repeated,
/// and a stage may qualify only from the attempt that POST was accepted for. The second stage starts only from one coherent
/// recorded Proposal and an unchanged source. Any failure, ambiguity or deadline ends the session without another submission;
/// the host and its children are stopped, both repositories are observed once more, and only then is the root handled.
/// </summary>
public sealed class QualificationSession(
    InvocationLedger ledger,
    IQualificationEnvironment environment,
    QualificationLimits limits,
    string sessionId)
{
    private const int ConsecutiveReadFailureLimit = 3;

    private SourceSnapshot? _sourceBaseline;
    private SourceSnapshot? _workspaceBaseline;

    public async Task<QualificationReport> RunAsync(CancellationToken cancellationToken)
    {
        var report = new QualificationReport(sessionId);
        using var overall = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        overall.CancelAfter(limits.Overall);
        try
        {
            await RunStagesAsync(report, overall.Token);
        }
        catch (OperationCanceledException)
        {
            report.Fail(FailureKind(report), "Cancelled");
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            report.Fail(FailureKind(report), $"Unexpected{exception.GetType().Name}");
        }

        await FinishAsync(report);
        return report;
    }

    private async Task RunStagesAsync(QualificationReport report, CancellationToken cancellationToken)
    {
        if (ledger.IsSpent())
        {
            report.Fail(QualificationResult.SessionSpent, "AllowanceAlreadySpent");
            return;
        }

        report.FixtureCreated = true;
        await environment.PrepareFixtureAsync(cancellationToken);
        var original = environment.SnapshotSource();

        report.HostStarted = true;
        await environment.StartHostAsync(cancellationToken);
        report.Targets = await environment.ObserveTargetsAsync(limits.Readiness, cancellationToken);
        if (!report.Targets.Accepted)
        {
            report.Fail(QualificationResult.Blocked, "TargetsNotReal");
            return;
        }

        var setup = await environment.RegisterAndPrepareAsync(cancellationToken);
        _sourceBaseline = environment.SnapshotSource();
        _workspaceBaseline = environment.SnapshotWorkspace();
        var setupDifferences = _sourceBaseline.DifferencesFrom(original, setup.WorkspaceBranchReference);
        report.NoteObservation(setupDifferences, complete: true, afterSubmission: false);
        if (setupDifferences.Count > 0)
        {
            report.Fail(QualificationResult.Blocked, "SourceChangedDuringSetup");
            return;
        }

        var planner = await RunStageAsync(AllowanceRole.Planner, null, report, cancellationToken);
        if (planner is null || Drifted(report))
        {
            return;
        }

        var reviewer = await RunStageAsync(AllowanceRole.Reviewer, planner.MessageId, report, cancellationToken);
        if (reviewer is null || Drifted(report))
        {
            return;
        }

        report.Qualify(reviewer.MessageType!);
    }

    private async Task<StageReading?> RunStageAsync(
        AllowanceRole role,
        Guid? proposalMessageId,
        QualificationReport report,
        CancellationToken cancellationToken)
    {
        var consumed = ledger.Consume(role, sessionId);
        if (consumed != ConsumeOutcome.Consumed)
        {
            var result = consumed == ConsumeOutcome.AlreadyConsumed && role == AllowanceRole.Planner
                ? QualificationResult.SessionSpent
                : FailureKind(report);
            report.Fail(result, $"{role}Allowance{consumed}");
            return null;
        }

        SetConsumed(report, role);
        SetSubmitted(report, role);
        var submission = await SubmitOnceAsync(role, proposalMessageId, cancellationToken);
        SetSubmission(report, role, submission.Outcome);
        if (submission.Outcome == SubmissionOutcome.Refused)
        {
            report.Fail(QualificationResult.Failed, $"{role}SubmissionRefused.{submission.Code ?? "Unspecified"}");
            return null;
        }

        if (submission.Outcome == SubmissionOutcome.Unknown)
        {
            report.Fail(QualificationResult.Failed, $"{role}SubmissionAmbiguous");
            SetReading(report, role, await TryReadOnceAsync(role, cancellationToken));
            return null;
        }

        if (submission.AttemptId is not { } accepted || accepted == Guid.Empty)
        {
            report.Fail(QualificationResult.Failed, $"{role}AcceptedAttemptMissing");
            SetReading(report, role, await TryReadOnceAsync(role, cancellationToken));
            return null;
        }

        SetAccepted(report, role, accepted);
        var reading = await AwaitTerminalAsync(role, accepted, report, cancellationToken);
        if (reading is not { AttemptFound: true } || (!reading.IsTerminal && reading.AttemptId == accepted))
        {
            report.Fail(QualificationResult.Failed, $"{role}DeadlineBeforeTerminal");
            return null;
        }

        var problem = StageVerifier.Verify(role, reading, proposalMessageId, accepted);
        if (problem is not null)
        {
            report.Fail(QualificationResult.Failed, $"{role}{problem}");
            return null;
        }

        return reading;
    }

    private async Task<SubmissionResult> SubmitOnceAsync(
        AllowanceRole role,
        Guid? proposalMessageId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await environment.SubmitAsync(role, proposalMessageId, cancellationToken);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return SubmissionResult.Unknown();
        }
    }

    /// <summary>Polls until the accepted attempt is terminal. An observation of any other attempt ends the wait at once: it is
    /// foreign evidence and is never substituted for the accepted one.</summary>
    private async Task<StageReading?> AwaitTerminalAsync(
        AllowanceRole role,
        Guid accepted,
        QualificationReport report,
        CancellationToken cancellationToken)
    {
        using var stage = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        stage.CancelAfter(limits.Stage);
        StageReading? latest = null;
        var failures = 0;
        try
        {
            while (true)
            {
                try
                {
                    latest = await environment.ReadStageAsync(role, stage.Token);
                    SetReading(report, role, latest);
                    failures = 0;
                    if (latest.IsTerminal || (latest.AttemptFound && latest.AttemptId != accepted))
                    {
                        return latest;
                    }
                }
                catch (Exception exception) when (exception is not (OperationCanceledException or OutOfMemoryException))
                {
                    if (++failures >= ConsecutiveReadFailureLimit)
                    {
                        return latest;
                    }
                }

                await Task.Delay(limits.PollInterval, stage.Token);
            }
        }
        catch (OperationCanceledException) when (DeadlineElapsed(stage, cancellationToken))
        {
            return latest;
        }
    }

    private async Task<StageReading?> TryReadOnceAsync(AllowanceRole role, CancellationToken cancellationToken)
    {
        try
        {
            return await environment.ReadStageAsync(role, cancellationToken);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return null;
        }
    }

    private static bool DeadlineElapsed(CancellationTokenSource stage, CancellationToken caller) =>
        stage.IsCancellationRequested && !caller.IsCancellationRequested;

    /// <summary>Observes the repository and the prepared worktree against their baselines, each on its own so that a failure to
    /// read one never suppresses the other. A repository that cannot be read is unproven, not unchanged. Returns true, after
    /// recording the failure code if none exists yet, when anything differs or could not be observed.</summary>
    private string? Observe(QualificationReport report)
    {
        var differences = new List<string>();
        var complete = true;
        try
        {
            differences.AddRange(environment.SnapshotSource().DifferencesFrom(_sourceBaseline!));
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            differences.Add("SourceUnreadable");
            complete = false;
        }

        try
        {
            var workspace = environment.SnapshotWorkspace().DifferencesFrom(_workspaceBaseline!);
            differences.AddRange(workspace.Select(name => "Workspace" + name));
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            differences.Add("WorkspaceUnreadable");
            complete = false;
        }

        report.NoteObservation(differences, complete, afterSubmission: true);
        if (differences.Count == 0)
        {
            return null;
        }

        var changed = differences.Any(difference => !difference.EndsWith("Unreadable", StringComparison.Ordinal));
        return changed ? "SourceChanged" : "SourceUnreadable";
    }

    private bool Drifted(QualificationReport report) => Observe(report) is { } code && Fail(report, code);

    private async Task FinishAsync(QualificationReport report)
    {
        if (!report.FixtureCreated)
        {
            return;
        }

        ShutdownReport shutdown;
        try
        {
            using var bound = new CancellationTokenSource(limits.Shutdown);
            shutdown = await environment.StopHostAsync(bound.Token);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            shutdown = ShutdownReport.Unproven;
        }

        report.Shutdown = shutdown;
        if (report.AnySubmitted && Observe(report) is { } code)
        {
            Fail(report, code);
            report.Disqualify(code);
        }

        var needsInspection = !report.Qualified && (report.ProviderMayHaveRun || report.SourceDifferences.Count > 0);
        var preserve = !shutdown.IsProven ? "ShutdownUnproven" : needsInspection ? "InspectionRequired" : null;
        report.Cleanup = preserve is null ? RemoveRoot() : CleanupReport.Preserved(preserve);
    }

    private CleanupReport RemoveRoot()
    {
        try
        {
            return environment.CleanUp();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return CleanupReport.Preserved("CleanupFailed");
        }
    }

    private static bool Fail(QualificationReport report, string code)
    {
        report.Fail(QualificationResult.Failed, code);
        return true;
    }

    private static QualificationResult FailureKind(QualificationReport report) =>
        report.PlannerConsumed || report.ReviewerConsumed ? QualificationResult.Failed : QualificationResult.Blocked;

    private static void SetConsumed(QualificationReport report, AllowanceRole role)
    {
        if (role == AllowanceRole.Planner)
        {
            report.PlannerConsumed = true;
        }
        else
        {
            report.ReviewerConsumed = true;
        }
    }

    private static void SetSubmitted(QualificationReport report, AllowanceRole role)
    {
        if (role == AllowanceRole.Planner)
        {
            report.PlannerSubmitted = true;
        }
        else
        {
            report.ReviewerSubmitted = true;
        }
    }

    private static void SetSubmission(QualificationReport report, AllowanceRole role, SubmissionOutcome outcome)
    {
        if (role == AllowanceRole.Planner)
        {
            report.PlannerSubmission = outcome;
        }
        else
        {
            report.ReviewerSubmission = outcome;
        }
    }

    private static void SetAccepted(QualificationReport report, AllowanceRole role, Guid accepted)
    {
        if (role == AllowanceRole.Planner)
        {
            report.PlannerAcceptedAttemptId = accepted;
        }
        else
        {
            report.ReviewerAcceptedAttemptId = accepted;
        }
    }

    private static void SetReading(QualificationReport report, AllowanceRole role, StageReading? reading)
    {
        if (role == AllowanceRole.Planner)
        {
            report.Planner = reading ?? report.Planner;
        }
        else
        {
            report.Reviewer = reading ?? report.Reviewer;
        }
    }
}
