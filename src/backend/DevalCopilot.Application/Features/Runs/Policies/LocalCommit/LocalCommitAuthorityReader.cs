using System.Security.Cryptography;
using System.Text;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Runs.Errors;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Policies.LocalCommit;

/// <summary>
/// The single fresh, untracked evaluation of ADR-0029's approval and exclusion gates. Every call re-reads the run, project,
/// workspace, lease, checkpoint, implementation lineage, the latest applicable CodeReviewer attempt and its actual approval
/// message, the human decision set and the enabled recipes with their latest executions. It is used at the pre-admission read, at
/// the locked admission seam, by the execution seam and by the read-only status query, so the decisions can never drift apart.
/// </summary>
internal sealed class LocalCommitAuthorityReader(IDevalCopilotDbContext dbContext)
{
    /// <summary>The identities the caller pinned. Null members select the single current candidate (status query only).</summary>
    internal sealed record Selection(Guid? CheckpointId, Guid? CodeReviewAttemptId, Guid? HumanCheckpointReviewId);

    internal sealed record Read(Error? Error, LocalCommitAuthority? Authority, Guid? CandidateCheckpointId = null);

    /// <param name="requiredWorkspaceStatus">Ready at admission and status; Committing at the locked reservation and execution seams.</param>
    /// <param name="ownOperationId">The operation being executed, excluded from the one-operation-per-run gate.</param>
    public async Task<Read> ReadAsync(
        Guid runId,
        Selection selection,
        WorkspaceStatus requiredWorkspaceStatus,
        Guid? ownOperationId,
        CancellationToken cancellationToken)
    {
        var run = await dbContext.Runs.AsNoTracking().SingleOrDefaultAsync(candidate => candidate.Id == runId, cancellationToken);
        if (run is null)
        {
            return Fail(LocalCommitErrors.RunNotFound());
        }

        if (await CurrentRunExecutionMode.ReadAsync(dbContext, runId, cancellationToken) != RunExecutionMode.ManualAgent
            || run.Lifecycle != RunLifecycle.Running)
        {
            return Fail(LocalCommitErrors.NotManualRunning());
        }

        var project = await dbContext.Projects.AsNoTracking()
            .Where(candidate => candidate.Id == run.ProjectId)
            .Select(candidate => new { candidate.CanonicalPath })
            .SingleOrDefaultAsync(cancellationToken);
        var workspace = await dbContext.GitWorkspaces.AsNoTracking()
            .Where(candidate => candidate.ProjectId == run.ProjectId)
            .OrderByDescending(candidate => candidate.WorkspaceNumber)
            .FirstOrDefaultAsync(cancellationToken);
        if (project is null || workspace is null || workspace.Status != requiredWorkspaceStatus)
        {
            return Fail(LocalCommitErrors.WorkspaceNotReady());
        }

        var lease = await dbContext.RepositoryMutationLeases.AsNoTracking().SingleOrDefaultAsync(
            candidate => candidate.WorkspaceId == workspace.Id && candidate.Status == LeaseStatus.Active, cancellationToken);
        if (lease is null || lease.ProjectId != run.ProjectId)
        {
            return Fail(LocalCommitErrors.LeaseNotActive());
        }

        var checkpoint = await dbContext.GitCheckpoints.AsNoTracking()
            .Where(candidate => candidate.WorkspaceId == workspace.Id)
            .OrderByDescending(candidate => candidate.CheckpointNumber)
            .FirstOrDefaultAsync(cancellationToken);
        if (checkpoint is null || (selection.CheckpointId is { } pinned && pinned != checkpoint.Id))
        {
            return Fail(LocalCommitErrors.CheckpointNotCurrent());
        }

        var changedFiles = await dbContext.GitChangedFiles.AsNoTracking()
            .Where(candidate => candidate.CheckpointId == checkpoint.Id)
            .OrderBy(candidate => candidate.Path)
            .ToListAsync(cancellationToken);

        var otherOperations = await dbContext.LocalCommitOperations.AsNoTracking()
            .Where(candidate => candidate.GitWorkspaceId == workspace.Id && candidate.Id != ownOperationId)
            .ToListAsync(cancellationToken);
        var expectedParent = LocalCommitHeadChain.ResolveTip(workspace.SourceCommitSha, otherOperations);
        if (expectedParent is null || !string.Equals(checkpoint.HeadCommitSha, expectedParent, StringComparison.Ordinal))
        {
            return Fail(LocalCommitErrors.ParentMismatch(), checkpoint.Id);
        }

        if (await dbContext.Attempts.AsNoTracking().AnyAsync(
                attempt => attempt.Status == AttemptStatus.Running
                    && (attempt.RunId == runId || attempt.AgentGitWorkspaceId == workspace.Id),
                cancellationToken)
            || await dbContext.VerificationExecutions.AsNoTracking().AnyAsync(
                execution => execution.GitWorkspaceId == workspace.Id && execution.Status == VerificationExecutionStatus.Running,
                cancellationToken))
        {
            return Fail(LocalCommitErrors.ActiveWork(), checkpoint.Id);
        }

        var runAttempts = await dbContext.Attempts.AsNoTracking()
            .Where(attempt => attempt.RunId == runId && attempt.Kind == AttemptKind.Agent)
            .ToListAsync(cancellationToken);
        var reviewCandidates = runAttempts
            .Where(attempt => attempt.AgentRole == AgentRole.CodeReviewer && attempt.AgentGitCheckpointId == checkpoint.Id)
            .OrderByDescending(attempt => attempt.AttemptNumber)
            .ToList();
        var review = selection.CodeReviewAttemptId is { } pinnedAttempt
            ? runAttempts.SingleOrDefault(attempt => attempt.Id == pinnedAttempt)
            : reviewCandidates.FirstOrDefault();
        if (review is null || review.AgentRole != AgentRole.CodeReviewer || review.Status != AttemptStatus.Completed
            || review.AgentOutcome != AgentOutcome.ReviewApproved
            || review.AgentResponseContract != AgentResponseContract.ImplementationReview
            || review.AgentGitWorkspaceId != workspace.Id || review.AgentGitCheckpointId != checkpoint.Id
            || !string.Equals(review.AgentCheckpointFingerprintSha256, checkpoint.FingerprintSha256, StringComparison.Ordinal))
        {
            return Fail(LocalCommitErrors.AgentApprovalMissing(), checkpoint.Id);
        }

        if (runAttempts.Any(attempt => attempt.AttemptNumber > review.AttemptNumber
                && attempt.AgentRole is AgentRole.CodeReviewer or AgentRole.Implementer))
        {
            return Fail(LocalCommitErrors.AgentApprovalNotLatest(), checkpoint.Id);
        }

        var inputs = await dbContext.AttemptInputMessages.AsNoTracking()
            .Where(input => input.AttemptId == review.Id)
            .OrderBy(input => input.Sequence)
            .ToListAsync(cancellationToken);
        if (inputs.Count != 1 || inputs[0].Sequence != 0)
        {
            return Fail(LocalCommitErrors.ImplementationNotCurrent(), checkpoint.Id);
        }

        var report = await dbContext.CollaborationMessages.AsNoTracking()
            .SingleOrDefaultAsync(message => message.Id == inputs[0].CollaborationMessageId && message.RunId == runId, cancellationToken);
        var lineage = report is null
            ? null
            : await ImplementerExecutionReportEligibility.ResolveAsync(
                dbContext, report, runId, workspace.Id, checkpoint.Id, cancellationToken);
        if (report is null || lineage is null)
        {
            return Fail(LocalCommitErrors.ImplementationNotCurrent(), checkpoint.Id);
        }

        var reviewMessages = await dbContext.CollaborationMessages.AsNoTracking()
            .Where(message => message.RunId == runId && message.AttemptId == review.Id)
            .ToListAsync(cancellationToken);
        var approvals = reviewMessages.Where(message => message.Type == CollaborationMessageType.ReviewApproval).ToList();
        if (approvals.Count != 1 || reviewMessages.Any(message => message.Type == CollaborationMessageType.ReviewFinding)
            || approvals[0].Provenance != CollaborationMessageProvenance.ProviderObserved
            || approvals[0].ActorKind != ParticipantKind.Agent || approvals[0].ActorAgentRole != AgentRole.CodeReviewer
            || approvals[0].InReplyToMessageId != report.Id)
        {
            return Fail(LocalCommitErrors.AgentApprovalMissing(), checkpoint.Id);
        }

        var verification = await ReadVerificationAsync(run.ProjectId, workspace, checkpoint, cancellationToken);
        if (verification.Error is not null)
        {
            return Fail(verification.Error, checkpoint.Id);
        }

        var members = verification.Members!;
        var claimed = await dbContext.AttemptVerificationEvidence.AsNoTracking()
            .Where(evidence => evidence.AttemptId == review.Id)
            .OrderBy(evidence => evidence.Sequence)
            .Select(evidence => new { evidence.VerificationCommandId, evidence.VerificationExecutionId })
            .ToListAsync(cancellationToken);
        if (!claimed.Select(item => (item.VerificationCommandId, item.VerificationExecutionId))
                .SequenceEqual(members.Select(member => (member.Command.Id, member.Execution.Id))))
        {
            return Fail(LocalCommitErrors.MembershipMismatch(), checkpoint.Id);
        }

        var reviews = await dbContext.CheckpointReviews.AsNoTracking()
            .Where(candidate => candidate.GitCheckpointId == checkpoint.Id)
            .ToListAsync(cancellationToken);
        var reviewIds = reviews.Select(candidate => candidate.Id).ToArray();
        var evidenceRows = await dbContext.CheckpointReviewEvidence.AsNoTracking()
            .Where(evidence => reviewIds.Contains(evidence.CheckpointReviewId))
            .ToListAsync(cancellationToken);
        var expectedMembership = members.Select(member => (member.Command.Id, member.Execution.Id)).ToHashSet();

        var agentReviews = reviews.Where(candidate => candidate.ActorKind == ReviewActorKind.FutureAgent
            && candidate.Decision == ReviewDecision.Approved
            && SameMembership(evidenceRows, candidate.Id, expectedMembership)).ToList();
        if (agentReviews.Count != 1 || !Coherent(agentReviews[0], workspace, checkpoint, run.ProjectId))
        {
            return Fail(LocalCommitErrors.AgentApprovalMissing(), checkpoint.Id);
        }

        var humanRows = reviews.Where(candidate => candidate.ActorKind == ReviewActorKind.Human).ToList();
        if (humanRows.Count == 0)
        {
            return Fail(LocalCommitErrors.HumanApprovalMissing(), checkpoint.Id);
        }

        if (humanRows.Any(candidate => candidate.Decision != ReviewDecision.Approved
                || !Coherent(candidate, workspace, checkpoint, run.ProjectId)
                || evidenceRows.Where(evidence => evidence.CheckpointReviewId == candidate.Id).Any(
                    evidence => evidence.VerificationExecutionStatus != VerificationExecutionStatus.Passed
                        || !string.Equals(
                            evidence.VerificationExecutionCheckpointFingerprintSha256, checkpoint.FingerprintSha256, StringComparison.Ordinal))))
        {
            return Fail(LocalCommitErrors.HumanDecisionNotApproved(), checkpoint.Id);
        }

        var human = selection.HumanCheckpointReviewId is { } pinnedHuman
            ? humanRows.SingleOrDefault(candidate => candidate.Id == pinnedHuman)
            : humanRows.OrderBy(candidate => candidate.Id).First();
        if (human is null)
        {
            return Fail(LocalCommitErrors.HumanApprovalMissing(), checkpoint.Id);
        }

        if (!SameMembership(evidenceRows, human.Id, expectedMembership))
        {
            return Fail(LocalCommitErrors.MembershipMismatch(), checkpoint.Id);
        }

        var humanDecisions = humanRows.OrderBy(candidate => candidate.Id).Select(candidate => new LocalCommitHumanDecision(
            candidate,
            LocalCommitMemberDigests.HumanDecision(
                candidate, evidenceRows.Where(evidence => evidence.CheckpointReviewId == candidate.Id)))).ToList();

        var authoritySha256 = ComputeAuthorityDigest(
            run, workspace, lease, checkpoint, review, approvals[0], report, agentReviews[0], human, humanDecisions, members, expectedParent);
        return new Read(
            null,
            new LocalCommitAuthority(
                run, project.CanonicalPath, workspace, lease, checkpoint, changedFiles, review, approvals[0], report,
                agentReviews[0], human, humanDecisions, members, expectedParent, authoritySha256),
            checkpoint.Id);
    }

    private sealed record VerificationRead(Error? Error, IReadOnlyList<LocalCommitVerificationMember>? Members);

    private async Task<VerificationRead> ReadVerificationAsync(
        Guid projectId, GitWorkspace workspace, GitCheckpoint checkpoint, CancellationToken cancellationToken)
    {
        var commands = await dbContext.VerificationCommands.AsNoTracking()
            .Where(command => command.ProjectId == projectId && command.IsEnabled)
            .OrderBy(command => command.CommandNumber)
            .ToListAsync(cancellationToken);
        if (commands.Count == 0)
        {
            return new VerificationRead(LocalCommitErrors.VerificationNotCurrent(), null);
        }

        var commandIds = commands.Select(command => command.Id).ToArray();
        var executions = await dbContext.VerificationExecutions.AsNoTracking()
            .Where(execution => commandIds.Contains(execution.VerificationCommandId)
                && execution.GitCheckpointId == checkpoint.Id
                && execution.CheckpointFingerprintSha256 == checkpoint.FingerprintSha256)
            .ToListAsync(cancellationToken);
        var latest = executions.GroupBy(execution => execution.VerificationCommandId)
            .ToDictionary(group => group.Key, group => group.OrderByDescending(execution => execution.ExecutionNumber).First());

        var members = new List<LocalCommitVerificationMember>(commands.Count);
        foreach (var command in commands)
        {
            if (!latest.TryGetValue(command.Id, out var execution)
                || execution.ProjectId != projectId || execution.GitWorkspaceId != workspace.Id
                || !string.Equals(execution.WorkspacePath, workspace.WorkspacePath, StringComparison.Ordinal)
                || execution.Status != VerificationExecutionStatus.Passed
                || execution.Outcome != VerificationExecutionOutcome.Exited || execution.ExitCode != 0
                || execution.DispatchedAtUtc is null || execution.CompletedAtUtc is null
                || !string.Equals(execution.CompletionFingerprintSha256, checkpoint.FingerprintSha256, StringComparison.Ordinal)
                || !SameSnapshot(command, execution))
            {
                return new VerificationRead(LocalCommitErrors.VerificationNotCurrent(), null);
            }

            members.Add(new LocalCommitVerificationMember(members.Count, command, execution, LocalCommitMemberDigests.Verification(
                command.Id, execution.Id, execution.ExecutionNumber, command.Name, command.ExecutablePath, command.TimeoutSeconds,
                command.Arguments, execution.CompletionFingerprintSha256)));
        }

        return new VerificationRead(null, members);
    }

    private static bool SameSnapshot(VerificationCommand command, VerificationExecution execution) =>
        string.Equals(command.Name, execution.CommandName, StringComparison.Ordinal)
        && string.Equals(command.ExecutablePath, execution.ExecutablePath, StringComparison.Ordinal)
        && command.TimeoutSeconds == execution.TimeoutSeconds
        && command.Arguments.SequenceEqual(execution.Arguments, StringComparer.Ordinal);

    private static bool Coherent(CheckpointReview review, GitWorkspace workspace, GitCheckpoint checkpoint, Guid projectId) =>
        review.ProjectId == projectId && review.GitWorkspaceId == workspace.Id && review.GitCheckpointId == checkpoint.Id
        && review.CheckpointNumber == checkpoint.CheckpointNumber
        && string.Equals(review.CheckpointFingerprintSha256, checkpoint.FingerprintSha256, StringComparison.Ordinal);

    private static bool SameMembership(
        IReadOnlyList<CheckpointReviewEvidence> evidenceRows, Guid reviewId, HashSet<(Guid, Guid)> expected)
    {
        var rows = evidenceRows.Where(evidence => evidence.CheckpointReviewId == reviewId).ToList();
        var actual = rows.Select(evidence => (evidence.VerificationCommandId, evidence.VerificationExecutionId)).ToHashSet();
        return rows.Count == expected.Count && actual.SetEquals(expected);
    }

    private static string ComputeAuthorityDigest(
        Run run,
        GitWorkspace workspace,
        RepositoryMutationLease lease,
        GitCheckpoint checkpoint,
        Attempt review,
        CollaborationMessage approval,
        CollaborationMessage report,
        CheckpointReview agentReview,
        CheckpointReview human,
        IReadOnlyList<LocalCommitHumanDecision> humanDecisions,
        IReadOnlyList<LocalCommitVerificationMember> members,
        string expectedParent)
    {
        var builder = new StringBuilder("local-commit-authority-v1\n");
        builder.Append(run.Id.ToString("N")).Append('|').Append(run.ProjectId.ToString("N")).Append('\n');
        builder.Append(workspace.Id.ToString("N")).Append('|').Append(workspace.WorkspacePath).Append('|')
            .Append(workspace.BranchName).Append('|').Append(workspace.SourceCommitSha).Append('\n');
        builder.Append(lease.Id.ToString("N")).Append('|').Append(lease.PhysicalVolumeSerialNumber).Append('|')
            .Append(Convert.ToHexString(lease.PhysicalFileId)).Append('\n');
        builder.Append(checkpoint.Id.ToString("N")).Append('|').Append(checkpoint.CheckpointNumber).Append('|')
            .Append(checkpoint.HeadCommitSha).Append('|').Append(checkpoint.FingerprintSha256).Append('\n');
        builder.Append(review.Id.ToString("N")).Append('|').Append(review.AttemptNumber).Append('|')
            .Append(approval.Id.ToString("N")).Append('|').Append(report.Id.ToString("N")).Append('|')
            .Append(agentReview.Id.ToString("N")).Append('|').Append(human.Id.ToString("N")).Append('\n');
        foreach (var decision in humanDecisions)
        {
            builder.Append("human:").Append(decision.Digest).Append('\n');
        }

        foreach (var member in members)
        {
            builder.Append("verification:").Append(member.Digest).Append('\n');
        }

        builder.Append("parent:").Append(expectedParent).Append('\n');
        return Sha256(builder.ToString());
    }

    private static string Sha256(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static Read Fail(Error error, Guid? candidateCheckpointId = null) => new(error, null, candidateCheckpointId);
}
