using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Runs.Policies.LocalCommit;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Queries.GetLocalDeliveryReceipt;

/// <summary>
/// Reconstructs the historical receipt of one completed local commit from the rows the operation was admitted against (ADR-0032).
/// Every fact is resolved by the identifier the operation recorded, never as "the latest" or "the current" record, so a later recipe
/// change, rerun, review, checkpoint or run cannot replace it. Anything missing, foreign, duplicated, reordered, over the limit or
/// whose recorded digest no longer matches makes the whole receipt <see cref="LocalDeliveryReceiptState.Unavailable"/>; nothing is
/// truncated, substituted or defaulted. It reads persisted rows only and never the repository, a process or a provider.
/// </summary>
internal sealed class LocalDeliveryReceiptReader(IDevalCopilotDbContext dbContext)
{
    public const int ReceiptVersion = 1;

    public const int MaximumVerificationMembers = 32;

    private static readonly GetLocalDeliveryReceiptQueryResult NotRecorded = new(LocalDeliveryReceiptState.NotRecorded, null);

    private static readonly GetLocalDeliveryReceiptQueryResult NotCompleted = new(LocalDeliveryReceiptState.NotCompleted, null);

    private static readonly GetLocalDeliveryReceiptQueryResult Unavailable = new(LocalDeliveryReceiptState.Unavailable, null);

    public async Task<GetLocalDeliveryReceiptQueryResult> ReadAsync(
        Guid runId, Guid projectId, string objective, CancellationToken cancellationToken)
    {
        // The stored status is classified by the database, never parsed into an enum first: a value this version does not know is
        // neither a completion nor a known non-completion, and must not fail the read.
        var operations = dbContext.LocalCommitOperations.AsNoTracking().Where(candidate => candidate.RunId == runId);
        var operationCount = await operations.CountAsync(cancellationToken);
        if (operationCount == 0)
        {
            return NotRecorded;
        }

        if (operationCount > 1)
        {
            return Unavailable;
        }

        if (!await operations.AnyAsync(candidate => candidate.Status == LocalCommitStatus.Completed, cancellationToken))
        {
            return await operations.AnyAsync(
                candidate => candidate.Status == LocalCommitStatus.Prepared || candidate.Status == LocalCommitStatus.Executing
                    || candidate.Status == LocalCommitStatus.Failed || candidate.Status == LocalCommitStatus.Interrupted
                    || candidate.Status == LocalCommitStatus.NeedsAttention,
                cancellationToken)
                ? NotCompleted
                : Unavailable;
        }

        var operation = await operations.SingleAsync(cancellationToken);

        var view = await ReconstructAsync(operation, runId, projectId, objective, cancellationToken);
        return view is null ? Unavailable : new GetLocalDeliveryReceiptQueryResult(LocalDeliveryReceiptState.Available, view);
    }

    private async Task<LocalDeliveryReceiptView?> ReconstructAsync(
        LocalCommitOperation operation, Guid runId, Guid projectId, string objective, CancellationToken cancellationToken)
    {
        if (operation.RunId != runId || operation.ProjectId != projectId || operation.CompletedAtUtc is not { } completedAtUtc
            || operation.ExecutionStartedAtUtc is null || !IsHex(operation.CommitSha, 40) || !IsHex(operation.ParentCommitSha, 40)
            || !IsHex(operation.TreeSha, 40) || !IsHex(operation.CheckpointFingerprintSha256, 64)
            || operation.CheckpointNumber < 1 || operation.ChangedPathCount is < 1 or > LocalCommitOperation.MaximumChangedPaths)
        {
            return null;
        }

        // The pinned run must itself record the completion this operation records. The lifecycle and stage are classified by the
        // database, so an unrecognized stored value is simply not a completion; this reads only the pinned run, never the latest.
        var runCompletedAtUtc = await dbContext.Runs.AsNoTracking()
            .Where(candidate => candidate.Id == runId && candidate.Lifecycle == RunLifecycle.Completed
                && candidate.Stage == RunStage.Completed)
            .Select(candidate => (DateTimeOffset?)candidate.LastAdvancedAtUtc)
            .SingleOrDefaultAsync(cancellationToken);
        if (runCompletedAtUtc != completedAtUtc)
        {
            return null;
        }

        var workspace = await dbContext.GitWorkspaces.AsNoTracking()
            .Where(candidate => candidate.Id == operation.GitWorkspaceId)
            .Select(candidate => new { candidate.ProjectId, candidate.BranchName, candidate.WorkspacePath })
            .SingleOrDefaultAsync(cancellationToken);
        var checkpoint = await dbContext.GitCheckpoints.AsNoTracking()
            .Where(candidate => candidate.Id == operation.GitCheckpointId)
            .Select(candidate => new
            {
                candidate.WorkspaceId,
                candidate.CheckpointNumber,
                candidate.FingerprintSha256,
                candidate.HeadCommitSha,
            })
            .SingleOrDefaultAsync(cancellationToken);
        if (workspace is null || checkpoint is null || workspace.ProjectId != projectId
            || !string.Equals(workspace.BranchName, operation.BranchName, StringComparison.Ordinal)
            || checkpoint.WorkspaceId != operation.GitWorkspaceId || checkpoint.CheckpointNumber != operation.CheckpointNumber
            || !string.Equals(checkpoint.FingerprintSha256, operation.CheckpointFingerprintSha256, StringComparison.Ordinal)
            || !string.Equals(checkpoint.HeadCommitSha, operation.ParentCommitSha, StringComparison.Ordinal))
        {
            return null;
        }

        var operationMembers = dbContext.LocalCommitAuthorityMembers.AsNoTracking().Where(member => member.OperationId == operation.Id);
        var verification = await operationMembers.Where(member => member.Kind == LocalCommitAuthorityMemberKind.Verification)
            .OrderBy(member => member.Sequence)
            .ToListAsync(cancellationToken);
        var humanMembers = await operationMembers.Where(member => member.Kind == LocalCommitAuthorityMemberKind.HumanReview)
            .ToListAsync(cancellationToken);
        if (verification.Count is < 1 or > MaximumVerificationMembers
            || verification.Count + humanMembers.Count != await operationMembers.CountAsync(cancellationToken)
            || verification.Select((member, index) => member.Sequence == index && member.CommandId is not null).Contains(false)
            || verification.Select(member => member.SubjectId).Distinct().Count() != verification.Count
            || verification.Select(member => member.CommandId).Distinct().Count() != verification.Count)
        {
            return null;
        }

        var humanMember = humanMembers.Where(member => member.SubjectId == operation.HumanCheckpointReviewId).ToList();
        if (humanMember.Count != 1)
        {
            return null;
        }

        var (report, implementerAttempt) = await ReadExecutionReportAsync(operation, runId, cancellationToken);
        var approval = report is null ? null : await ReadCodeReviewApprovalAsync(operation, runId, report, cancellationToken);
        if (report is null || implementerAttempt is null || approval is null)
        {
            return null;
        }

        var review = approval.Value.Review;
        var expected = verification.Select(member => (member.CommandId!.Value, member.SubjectId)).ToList();
        var claimed = await dbContext.AttemptVerificationEvidence.AsNoTracking()
            .Where(evidence => evidence.AttemptId == review.Id)
            .OrderBy(evidence => evidence.Sequence)
            .Select(evidence => new { evidence.Sequence, evidence.VerificationCommandId, evidence.VerificationExecutionId })
            .ToListAsync(cancellationToken);
        if (claimed.Count != expected.Count
            || claimed.Select((item, index) => item.Sequence == index).Contains(false)
            || !claimed.Select(item => (item.VerificationCommandId, item.VerificationExecutionId)).SequenceEqual(expected))
        {
            return null;
        }

        var read = await ReadExecutionsAsync(operation, workspace.WorkspacePath, verification, cancellationToken);
        if (read is not { } executions)
        {
            return null;
        }

        var human = await ReadHumanReviewAsync(operation, humanMember[0].Digest, expected, executions.ById, cancellationToken);
        if (human is null)
        {
            return null;
        }

        return new LocalDeliveryReceiptView(
            ReceiptVersion,
            runId,
            operation.Id,
            objective,
            operation.CommitSha,
            operation.ParentCommitSha,
            operation.TreeSha,
            operation.BranchName,
            completedAtUtc,
            new LocalDeliveryCheckpointView(
                operation.GitCheckpointId, operation.CheckpointNumber, operation.CheckpointFingerprintSha256, operation.ChangedPathCount),
            report.Id,
            new LocalDeliveryCodeReviewView(review.Id, review.AttemptNumber, approval.Value.Message.Id),
            new LocalDeliveryHumanReviewView(human.Id, human.Decision.ToString()),
            executions.Views);
    }

    private async Task<(CollaborationMessage? Report, Attempt? Implementer)> ReadExecutionReportAsync(
        LocalCommitOperation operation, Guid runId, CancellationToken cancellationToken)
    {
        var report = await dbContext.CollaborationMessages.AsNoTracking()
            .SingleOrDefaultAsync(message => message.Id == operation.ExecutionReportMessageId, cancellationToken);
        if (report is null || report.RunId != runId || report.Type != CollaborationMessageType.ExecutionReport
            || report.Provenance != CollaborationMessageProvenance.ProviderObserved || report.ActorKind != ParticipantKind.Agent
            || report.ActorAgentRole != AgentRole.Implementer || report.AttemptId is not { } attemptId)
        {
            return (null, null);
        }

        var implementer = await dbContext.Attempts.AsNoTracking()
            .SingleOrDefaultAsync(attempt => attempt.Id == attemptId, cancellationToken);
        if (implementer is null || implementer.RunId != runId || implementer.Kind != AttemptKind.Agent
            || implementer.AgentRole != AgentRole.Implementer || implementer.Status != AttemptStatus.Completed
            || implementer.AgentProvider != report.ActorAgentProvider
            || implementer.AgentGitWorkspaceId != operation.GitWorkspaceId
            || implementer.AgentResultGitCheckpointId != operation.GitCheckpointId
            || (implementer.AgentResponseContract, implementer.AgentOutcome) is not (
                (AgentResponseContract.ImplementationReport, AgentOutcome.Implemented)
                or (AgentResponseContract.ReviewCorrection, AgentOutcome.CorrectionApplied)))
        {
            return (null, null);
        }

        var reports = await dbContext.CollaborationMessages.AsNoTracking().CountAsync(
            message => message.AttemptId == implementer.Id && message.Type == CollaborationMessageType.ExecutionReport
                && message.Provenance == CollaborationMessageProvenance.ProviderObserved,
            cancellationToken);
        return reports == 1 ? (report, implementer) : (null, null);
    }

    private async Task<(Attempt Review, CollaborationMessage Message)?> ReadCodeReviewApprovalAsync(
        LocalCommitOperation operation, Guid runId, CollaborationMessage report, CancellationToken cancellationToken)
    {
        var review = await dbContext.Attempts.AsNoTracking()
            .SingleOrDefaultAsync(attempt => attempt.Id == operation.CodeReviewAttemptId, cancellationToken);
        if (review is null || review.RunId != runId || review.Kind != AttemptKind.Agent || review.AgentRole != AgentRole.CodeReviewer
            || review.Status != AttemptStatus.Completed || review.AgentOutcome != AgentOutcome.ReviewApproved
            || review.AgentResponseContract != AgentResponseContract.ImplementationReview
            || review.AttemptNumber < 1 || review.AgentProvider is null
            || review.AgentGitWorkspaceId != operation.GitWorkspaceId || review.AgentGitCheckpointId != operation.GitCheckpointId
            || !string.Equals(review.AgentCheckpointFingerprintSha256, operation.CheckpointFingerprintSha256, StringComparison.Ordinal))
        {
            return null;
        }

        var inputs = await dbContext.AttemptInputMessages.AsNoTracking()
            .Where(input => input.AttemptId == review.Id)
            .ToListAsync(cancellationToken);
        if (inputs.Count != 1 || inputs[0].Sequence != 0 || inputs[0].CollaborationMessageId != report.Id)
        {
            return null;
        }

        var messages = await dbContext.CollaborationMessages.AsNoTracking()
            .Where(message => message.RunId == runId && message.AttemptId == review.Id)
            .ToListAsync(cancellationToken);
        var approvals = messages.Where(message => message.Type == CollaborationMessageType.ReviewApproval).ToList();
        if (approvals.Count != 1 || messages.Any(message => message.Type == CollaborationMessageType.ReviewFinding)
            || approvals[0].Id != operation.CodeReviewApprovalMessageId
            || approvals[0].Provenance != CollaborationMessageProvenance.ProviderObserved
            || approvals[0].ActorKind != ParticipantKind.Agent || approvals[0].ActorAgentRole != AgentRole.CodeReviewer
            || approvals[0].ActorAgentProvider != review.AgentProvider
            || approvals[0].InReplyToMessageId != report.Id)
        {
            return null;
        }

        return (review, approvals[0]);
    }

    private async Task<(List<LocalDeliveryVerificationView> Views, Dictionary<Guid, VerificationExecution> ById)?> ReadExecutionsAsync(
        LocalCommitOperation operation,
        string workspacePath,
        List<LocalCommitAuthorityMember> verification,
        CancellationToken cancellationToken)
    {
        var executionIds = verification.Select(member => member.SubjectId).ToArray();
        var executions = await dbContext.VerificationExecutions.AsNoTracking()
            .Where(execution => executionIds.Contains(execution.Id))
            .ToDictionaryAsync(execution => execution.Id, cancellationToken);
        if (executions.Count != verification.Count)
        {
            return null;
        }

        var views = new List<LocalDeliveryVerificationView>(verification.Count);
        foreach (var member in verification)
        {
            var execution = executions[member.SubjectId];
            if (execution.ExecutionNumber < 1 || execution.ProjectId != operation.ProjectId || execution.GitWorkspaceId != operation.GitWorkspaceId
                || execution.GitCheckpointId != operation.GitCheckpointId || execution.VerificationCommandId != member.CommandId
                || !string.Equals(execution.CheckpointFingerprintSha256, operation.CheckpointFingerprintSha256, StringComparison.Ordinal)
                || !string.Equals(execution.WorkspacePath, workspacePath, StringComparison.Ordinal)
                || execution.Status != VerificationExecutionStatus.Passed || execution.Outcome != VerificationExecutionOutcome.Exited
                || execution.ExitCode != 0 || execution.DispatchedAtUtc is null
                || execution.CompletedAtUtc is not { } completedAtUtc
                || !string.Equals(execution.CompletionFingerprintSha256, operation.CheckpointFingerprintSha256, StringComparison.Ordinal)
                || !string.Equals(
                    member.Digest,
                    LocalCommitMemberDigests.Verification(
                        execution.VerificationCommandId, execution.Id, execution.ExecutionNumber, execution.CommandName,
                        execution.ExecutablePath, execution.TimeoutSeconds, execution.Arguments, execution.CompletionFingerprintSha256),
                    StringComparison.Ordinal))
            {
                return null;
            }

            views.Add(new LocalDeliveryVerificationView(
                member.Sequence, execution.VerificationCommandId, execution.Id, execution.ExecutionNumber, execution.CommandName,
                execution.Status.ToString(), 0, completedAtUtc));
        }

        return (views, executions);
    }

    private async Task<CheckpointReview?> ReadHumanReviewAsync(
        LocalCommitOperation operation,
        string recordedDigest,
        List<(Guid CommandId, Guid ExecutionId)> expected,
        Dictionary<Guid, VerificationExecution> executions,
        CancellationToken cancellationToken)
    {
        var reviewIds = new[] { operation.AgentCheckpointReviewId, operation.HumanCheckpointReviewId };
        var reviews = await dbContext.CheckpointReviews.AsNoTracking()
            .Where(candidate => reviewIds.Contains(candidate.Id))
            .ToDictionaryAsync(candidate => candidate.Id, cancellationToken);
        var evidence = await dbContext.CheckpointReviewEvidence.AsNoTracking()
            .Where(row => reviewIds.Contains(row.CheckpointReviewId))
            .ToListAsync(cancellationToken);
        if (!reviews.TryGetValue(operation.AgentCheckpointReviewId, out var agent)
            || !reviews.TryGetValue(operation.HumanCheckpointReviewId, out var human))
        {
            return null;
        }

        var humanEvidence = evidence.Where(row => row.CheckpointReviewId == human.Id).ToList();
        var agentEvidence = evidence.Where(row => row.CheckpointReviewId == agent.Id).ToList();
        return agent.ActorKind == ReviewActorKind.FutureAgent && agent.Decision == ReviewDecision.Approved
            && Coherent(agent, operation) && SameMembership(agentEvidence, expected) && SameSnapshots(agentEvidence, executions)
            && human.ActorKind == ReviewActorKind.Human && human.Decision == ReviewDecision.Approved
            && Coherent(human, operation) && SameMembership(humanEvidence, expected) && SameSnapshots(humanEvidence, executions)
            && string.Equals(recordedDigest, LocalCommitMemberDigests.HumanDecision(human, humanEvidence), StringComparison.Ordinal)
                ? human
                : null;
    }

    private static bool Coherent(CheckpointReview review, LocalCommitOperation operation) =>
        review.ProjectId == operation.ProjectId && review.GitWorkspaceId == operation.GitWorkspaceId
        && review.GitCheckpointId == operation.GitCheckpointId && review.CheckpointNumber == operation.CheckpointNumber
        && string.Equals(review.CheckpointFingerprintSha256, operation.CheckpointFingerprintSha256, StringComparison.Ordinal);

    private static bool SameMembership(List<CheckpointReviewEvidence> rows, List<(Guid CommandId, Guid ExecutionId)> expected) =>
        rows.Count == expected.Count
        && rows.Select(row => (row.VerificationCommandId, row.VerificationExecutionId)).ToHashSet().SetEquals(expected);

    // A review stores its own copy of each execution's facts. The member digest binds only identities, so every stored field is
    // compared with the pinned execution here; a contradiction rejects the receipt and the execution row never stands in for it.
    private static bool SameSnapshots(List<CheckpointReviewEvidence> rows, Dictionary<Guid, VerificationExecution> executions) =>
        rows.All(row => executions.TryGetValue(row.VerificationExecutionId, out var execution)
            && row.VerificationCommandId == execution.VerificationCommandId
            && row.VerificationExecutionNumber == execution.ExecutionNumber
            && row.VerificationExecutionStatus == execution.Status
            && row.VerificationExecutionOutcome == execution.Outcome
            && row.VerificationExecutionExitCode == execution.ExitCode
            && string.Equals(row.VerificationExecutionCheckpointFingerprintSha256, execution.CheckpointFingerprintSha256, StringComparison.Ordinal));

    private static bool IsHex(string? value, int length) =>
        value is not null && value.Length == length && value.All(character => character is (>= '0' and <= '9') or (>= 'a' and <= 'f'));
}
