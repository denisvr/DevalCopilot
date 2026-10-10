namespace DevalCopilot.Api.IntegrationTests.ManualQualification;

/// <summary>The facts of one session, collected as closed values only. The first failure is preserved and never overwritten by a
/// later one; <see cref="SafeSummary"/> is the only thing that turns this into text.</summary>
public sealed class QualificationReport(string sessionId)
{
    private readonly List<string> _sourceChecks = [];
    private bool _sourceUnproven;

    public string SessionId { get; } = sessionId;

    public QualificationResult Result { get; private set; } = QualificationResult.Blocked;

    public string Code { get; private set; } = "NotStarted";

    public string? Verdict { get; private set; }

    public TargetJudgement? Targets { get; set; }

    public bool FixtureCreated { get; set; }

    public bool HostStarted { get; set; }

    public bool PlannerConsumed { get; set; }

    public bool ReviewerConsumed { get; set; }

    public bool PlannerSubmitted { get; set; }

    public bool ReviewerSubmitted { get; set; }

    public SubmissionOutcome? PlannerSubmission { get; set; }

    public SubmissionOutcome? ReviewerSubmission { get; set; }

    public Guid? PlannerAcceptedAttemptId { get; set; }

    public Guid? ReviewerAcceptedAttemptId { get; set; }

    public StageReading? Planner { get; set; }

    public StageReading? Reviewer { get; set; }

    public int SourceChecks { get; private set; }

    /// <summary>True once a complete observation of both repositories was made after the last submission.</summary>
    public bool PostStageObserved { get; private set; }

    public IReadOnlyList<string> SourceDifferences => _sourceChecks;

    public ShutdownReport? Shutdown { get; set; }

    public CleanupReport? Cleanup { get; set; }

    /// <summary>Unchanged is claimed only from complete observations: an unreadable repository, or a submitted stage that was never
    /// observed afterwards, leaves the source unproven, which is never the same as unchanged.</summary>
    public bool SourceProven => SourceChecks > 0 && !_sourceUnproven && (!AnySubmitted || PostStageObserved);

    public bool Qualified => Result == QualificationResult.Qualified;

    public bool AnySubmitted => PlannerSubmitted || ReviewerSubmitted;

    /// <summary>True once any observation shows that a provider may have been launched.</summary>
    public bool ProviderMayHaveRun =>
        AnySubmitted || Planner is { Dispatched: true } || Reviewer is { Dispatched: true };

    public int ExitCode => Result switch
    {
        QualificationResult.Qualified => Cleanup is null || Cleanup.Removed ? 0 : 6,
        QualificationResult.SessionSpent => 3,
        QualificationResult.Blocked => 4,
        _ => 5,
    };

    public void NoteObservation(IReadOnlyList<string> differences, bool complete, bool afterSubmission)
    {
        SourceChecks++;
        _sourceChecks.AddRange(differences.Where(difference => !_sourceChecks.Contains(difference)));
        _sourceUnproven |= !complete;
        if (afterSubmission)
        {
            PostStageObserved = complete;
        }
    }

    /// <summary>What is truthfully known about the provider invocation of one role. Dispatch is not execution: only host-measured
    /// process evidence of the attempt this role's POST was accepted for proves an observed execution.</summary>
    public string InvocationState(AllowanceRole role)
    {
        var (submitted, outcome, accepted, reading) = role == AllowanceRole.Planner
            ? (PlannerSubmitted, PlannerSubmission, PlannerAcceptedAttemptId, Planner)
            : (ReviewerSubmitted, ReviewerSubmission, ReviewerAcceptedAttemptId, Reviewer);
        if (!submitted)
        {
            return "NotAttempted";
        }

        if (outcome == SubmissionOutcome.Refused)
        {
            return "RefusedBeforeDispatch";
        }

        if (reading is not { AttemptFound: true } || accepted is null || reading.AttemptId != accepted)
        {
            return "Unknown";
        }

        if (reading.ExecutionObserved)
        {
            return "ExecutionObserved";
        }

        return reading.Dispatched ? "DispatchedNoExecutionEvidence" : "NotDispatched";
    }

    public void Fail(QualificationResult result, string code)
    {
        if (Code == "NotStarted")
        {
            Result = result;
            Code = code;
        }
    }

    public void Qualify(string verdict)
    {
        Result = QualificationResult.Qualified;
        Code = "Qualified";
        Verdict = verdict;
    }

    /// <summary>A qualified result that a later observation contradicts is not qualified.</summary>
    public void Disqualify(string code)
    {
        if (Qualified)
        {
            Result = QualificationResult.Failed;
            Code = code;
            Verdict = null;
        }
    }
}
