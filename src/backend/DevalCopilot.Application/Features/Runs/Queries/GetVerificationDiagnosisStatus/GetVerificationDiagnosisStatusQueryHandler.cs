using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Runs.Policies.VerificationDiagnosis;
using DevalCopilot.Application.Features.Runs.Queries.GetAgentAttemptStatus;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Queries.GetVerificationDiagnosisStatus;

public sealed class GetVerificationDiagnosisStatusQueryHandler(IDevalCopilotDbContext dbContext)
    : IQueryHandler<GetVerificationDiagnosisStatusQuery, Result<VerificationDiagnosisStatusQueryResult>>
{
    // Mirrors the fixed CLI configuration the shared CodexProcessInvoker applies — static configuration facts, never a
    // provider-observed result and never invocation eligibility.
    private const string ConfiguredCodexCommandSandbox = "read-only";
    private const string ConfiguredCodexRolloutPersistence = "Disabled";

    public async Task<Result<VerificationDiagnosisStatusQueryResult>> HandleAsync(
        GetVerificationDiagnosisStatusQuery query, CancellationToken cancellationToken)
    {
        var run = await dbContext.Runs.AsNoTracking().SingleOrDefaultAsync(candidate => candidate.Id == query.RunId, cancellationToken);
        if (run is null)
        {
            return Result<VerificationDiagnosisStatusQueryResult>.Failure(
                Error.NotFound("runs.not_found", "The requested run was not found."));
        }

        ImplementerExecutionReportEligibility.Snapshot snapshot;
        try
        {
            snapshot = await ImplementerExecutionReportEligibility.LoadSnapshotAsync(dbContext, run.Id, cancellationToken);
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException or InvalidOperationException)
        {
            return InvalidAssignment();
        }

        var correctionAttemptsUsed = snapshot.AttemptsById.Values.Count(candidate =>
            candidate.Kind == AttemptKind.Agent && candidate.AgentResponseContract == AgentResponseContract.ReviewCorrection);

        var workspace = await dbContext.GitWorkspaces.AsNoTracking()
            .Where(candidate => candidate.ProjectId == run.ProjectId)
            .OrderByDescending(candidate => candidate.WorkspaceNumber)
            .FirstOrDefaultAsync(cancellationToken);
        var currentCheckpoint = workspace is null
            ? null
            : await dbContext.GitCheckpoints.AsNoTracking()
                .Where(candidate => candidate.WorkspaceId == workspace.Id)
                .OrderByDescending(candidate => candidate.CheckpointNumber)
                .FirstOrDefaultAsync(cancellationToken);

        var (diagnosableReportId, unavailableCode) = await ResolveDiagnosableAsync(run, workspace, currentCheckpoint, snapshot, cancellationToken);

        var attempt = snapshot.AttemptsById.Values
            .Where(candidate => candidate.Kind == AttemptKind.Agent
                && candidate.AgentRole == AgentRole.CodeReviewer
                && candidate.AgentResponseContract == AgentResponseContract.VerificationDiagnosis)
            .OrderByDescending(candidate => candidate.AttemptNumber)
            .FirstOrDefault();
        if (attempt is null)
        {
            return Result<VerificationDiagnosisStatusQueryResult>.Success(VerificationDiagnosisStatusQueryResult.NoAttempt(
                run.MaximumReviewCorrectionAttempts, correctionAttemptsUsed, diagnosableReportId, unavailableCode));
        }

        var assignment = attempt.GetAssignmentSnapshot();
        if (assignment is null)
        {
            return InvalidAssignment();
        }

        var artifacts = await dbContext.Artifacts.AsNoTracking()
            .Where(artifact => artifact.AttemptId == attempt.Id)
            .Select(artifact => new AgentAttemptArtifactMetadata(artifact.Purpose, artifact.ByteLength, artifact.Truncated, artifact.CaptureOutcome))
            .ToArrayAsync(cancellationToken);
        var reportId = await VerificationDiagnosisInputIdentity.ReadPinnedExecutionReportIdAsync(dbContext, attempt.Id, cancellationToken);
        var members = await ReadMembersAsync(attempt.Id, cancellationToken);

        var findingCount = snapshot.Messages.Count(message => message.AttemptId == attempt.Id && message.Type == CollaborationMessageType.ReviewFinding);
        var escalationMessageId = snapshot.Messages
            .Where(message => message.AttemptId == attempt.Id && message.Type == CollaborationMessageType.Escalation)
            .Select(message => (Guid?)message.Id)
            .FirstOrDefault();

        var correctionApplicable = false;
        Attempt? correction = null;
        Guid? reviewableReportId = null;
        DiagnosisCorrectionEscalation? escalation = null;
        if (attempt.Status == AttemptStatus.Completed
            && attempt.AgentOutcome == AgentOutcome.DiagnosisFindingsRecorded
            && workspace is { Status: WorkspaceStatus.Ready }
            && currentCheckpoint is not null
            && attempt.AgentGitCheckpointId == currentCheckpoint.Id)
        {
            correctionApplicable = VerificationDiagnosisEligibility.ResolveForAttempt(
                    snapshot, attempt, run.Id, workspace.Id, currentCheckpoint.Id) is not null
                && await VerificationDiagnosisApplicability.EvaluateAsync(dbContext, attempt, cancellationToken)
                    == VerificationDiagnosisApplicability.Verdict.Applicable;
        }

        if (attempt.AgentOutcome == AgentOutcome.DiagnosisFindingsRecorded)
        {
            correction = FindLatestCorrectionOf(snapshot, attempt);
            if (correction is { Status: AttemptStatus.Completed, AgentOutcome: AgentOutcome.CorrectionApplied })
            {
                reviewableReportId = ResolveReportOwnedByAttempt(snapshot, correction);
            }

            escalation = await dbContext.DiagnosisCorrectionEscalations.AsNoTracking()
                .SingleOrDefaultAsync(candidate => candidate.VerificationDiagnosisAttemptId == attempt.Id, cancellationToken);
        }

        var isCoherentAssignment = VerificationDiagnosisPolicy.HasExactTuple(attempt);

        return Result<VerificationDiagnosisStatusQueryResult>.Success(new VerificationDiagnosisStatusQueryResult(
            true,
            attempt.Id,
            attempt.AttemptNumber,
            reportId,
            attempt.Status,
            attempt.AgentOutcome,
            attempt.ClaimedAtUtc,
            attempt.AgentDispatchedAtUtc,
            attempt.CompletedAtUtc,
            artifacts,
            members,
            findingCount,
            escalationMessageId,
            correctionApplicable,
            correction?.Id,
            correction?.AttemptNumber,
            correction?.Status,
            correction?.AgentOutcome,
            reviewableReportId,
            run.MaximumReviewCorrectionAttempts,
            correctionAttemptsUsed,
            correctionAttemptsUsed >= run.MaximumReviewCorrectionAttempts,
            escalation?.Id,
            escalation?.CollaborationMessageId,
            diagnosableReportId,
            unavailableCode,
            attempt.GetAgentProcessExecutionEvidence(),
            attempt.AgentTimeout,
            attempt.GetAgentTokenUsageEvidence(),
            isCoherentAssignment ? ConfiguredCodexCommandSandbox : null,
            isCoherentAssignment ? ConfiguredCodexRolloutPersistence : null));
    }

    private static Result<VerificationDiagnosisStatusQueryResult> InvalidAssignment() =>
        Result<VerificationDiagnosisStatusQueryResult>.Failure(
            Error.Failure("agent_attempts.invalid_assignment", "Verification diagnosis assignment metadata is unavailable."));

    private async Task<(Guid? ReportId, string? Code)> ResolveDiagnosableAsync(
        Run run,
        GitWorkspace? workspace,
        GitCheckpoint? currentCheckpoint,
        ImplementerExecutionReportEligibility.Snapshot snapshot,
        CancellationToken cancellationToken)
    {
        if (run.Lifecycle != RunLifecycle.Running || workspace is not { Status: WorkspaceStatus.Ready } || currentCheckpoint is null)
        {
            return (null, "verification_diagnosis.workspace_not_ready");
        }

        var report = ResolveCurrentReport(snapshot, run.Id, workspace.Id, currentCheckpoint.Id);
        if (report is null)
        {
            return (null, "verification_diagnosis.no_current_execution_report");
        }

        try
        {
            var selection = await VerificationDiagnosisEvidence.ReadAsync(
                dbContext, run.ProjectId, workspace.Id, currentCheckpoint, asNoTracking: true, cancellationToken);
            if (selection.Error is { } error)
            {
                return (null, error.Code);
            }

            return await VerificationDiagnosisInputIdentity.HasCompetingSuccessfulDiagnosisAsync(
                dbContext, run.Id, Guid.Empty, report.Id, selection.Value!.OrderedExecutionIds, cancellationToken)
                ? (null, "agent_attempts.already_diagnosed")
                : (report.Id, null);
        }
        catch (InvalidOperationException)
        {
            return (null, VerificationDiagnosisEvidence.NotDiagnosableCode);
        }
    }

    private async Task<IReadOnlyList<VerificationDiagnosisMember>> ReadMembersAsync(Guid attemptId, CancellationToken cancellationToken)
    {
        var pinned = await VerificationDiagnosisInputIdentity.ReadPinnedPairsAsync(dbContext, attemptId, cancellationToken);
        var executionIds = pinned.Select(pair => pair.ExecutionId).ToArray();
        var executions = await dbContext.VerificationExecutions.AsNoTracking()
            .Where(execution => executionIds.Contains(execution.Id))
            .ToDictionaryAsync(execution => execution.Id, cancellationToken);
        return pinned
            .Select((pair, index) => executions.TryGetValue(pair.ExecutionId, out var execution)
                ? new VerificationDiagnosisMember(index + 1, execution.CommandName, execution.ExecutionNumber, execution.Status.ToString(), execution.ExitCode)
                : null)
            .OfType<VerificationDiagnosisMember>()
            .ToArray();
    }

    /// <summary>The valid ExecutionReport whose owning Implementer attempt's result is the workspace's current checkpoint.</summary>
    private static CollaborationMessage? ResolveCurrentReport(
        ImplementerExecutionReportEligibility.Snapshot snapshot, Guid runId, Guid workspaceId, Guid currentCheckpointId)
    {
        var owner = snapshot.AttemptsById.Values
            .Where(candidate => candidate.RunId == runId
                && candidate.Kind == AttemptKind.Agent
                && candidate.AgentRole == AgentRole.Implementer
                && candidate.Status == AttemptStatus.Completed
                && candidate.AgentResultGitCheckpointId == currentCheckpointId
                && candidate.AgentGitWorkspaceId == workspaceId)
            .OrderByDescending(candidate => candidate.AttemptNumber)
            .FirstOrDefault();
        return owner is null ? null : ResolveReportOwnedByAttemptMessage(snapshot, owner);
    }

    private static Guid? ResolveReportOwnedByAttempt(ImplementerExecutionReportEligibility.Snapshot snapshot, Attempt attempt) =>
        ResolveReportOwnedByAttemptMessage(snapshot, attempt)?.Id;

    private static CollaborationMessage? ResolveReportOwnedByAttemptMessage(
        ImplementerExecutionReportEligibility.Snapshot snapshot, Attempt attempt)
    {
        if (attempt.AgentGitWorkspaceId is not { } workspaceId
            || attempt.AgentResultGitCheckpointId is not { } resultCheckpointId
            || snapshot.CurrentCheckpointFor(workspaceId) != resultCheckpointId)
        {
            return null;
        }

        var reports = snapshot.Messages
            .Where(message => message.AttemptId == attempt.Id
                && message.Type == CollaborationMessageType.ExecutionReport
                && message.Provenance == CollaborationMessageProvenance.ProviderObserved)
            .ToArray();
        return reports.Length == 1
            && ImplementerExecutionReportEligibility.Resolve(snapshot, reports[0], attempt.RunId, workspaceId, resultCheckpointId) is not null
            ? reports[0]
            : null;
    }

    /// <summary>The latest ReviewCorrection attempt whose finding inputs are exactly this diagnosis's complete findings.</summary>
    private static Attempt? FindLatestCorrectionOf(ImplementerExecutionReportEligibility.Snapshot snapshot, Attempt diagnosis)
    {
        var findingIds = snapshot.Messages
            .Where(message => message.AttemptId == diagnosis.Id && message.Type == CollaborationMessageType.ReviewFinding)
            .OrderBy(message => message.Sequence)
            .Select(message => message.Id)
            .ToArray();
        if (findingIds.Length == 0)
        {
            return null;
        }

        return snapshot.AttemptsById.Values
            .Where(candidate => candidate.RunId == diagnosis.RunId
                && candidate.Kind == AttemptKind.Agent
                && candidate.AgentRole == AgentRole.Implementer
                && candidate.AgentResponseContract == AgentResponseContract.ReviewCorrection
                && snapshot.InputsFor(candidate.Id).Skip(1).Select(input => input.CollaborationMessageId).SequenceEqual(findingIds))
            .OrderByDescending(candidate => candidate.AttemptNumber)
            .FirstOrDefault();
    }
}
