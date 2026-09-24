using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Application.Features.Runs;
using DevalCopilot.Domain.Features.EnvironmentReadiness;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Commands.CreateCodeReviewAttempt;

/// <summary>
/// Claims one durable Codex code-review attempt. Mirrors
/// <c>CreateChallengeResolutionAttemptCommandHandler</c>'s workspace/lease/checkpoint eligibility
/// chain and sealed-manifest/persistence-race handling exactly, plus the additional closed chain of
/// checks that: the named ExecutionReport is real, current, and owned by a successful Claude
/// implementation attempt whose result checkpoint is exactly the workspace's current checkpoint;
/// every currently enabled verification command has a latest terminal execution bound to that exact
/// checkpoint, and every one of them Passed; and this exact ExecutionReport-plus-verification-set
/// input identity is not already successfully reviewed. Every failure fails closed with a stable,
/// path-free reason code — never a partial claim, and never an arbitrarily selected verification
/// execution when several enabled commands exist.
/// </summary>
public sealed class CreateCodeReviewAttemptCommandHandler(
    IDevalCopilotDbContext dbContext,
    IGitWorkspaceEvidenceReader evidenceReader,
    IArtifactStore artifactStore,
    TimeProvider timeProvider)
    : ICommandHandler<CreateCodeReviewAttemptCommand, Result<CreateCodeReviewAttemptCommandResult>>
{
    private const int MaxContextManifestBytes = 32 * 1024;

    private static readonly TimeSpan InvocationTimeout = TimeSpan.FromMinutes(10);
    private const int MaxBytesPerStream = 256 * 1024;
    private const int MaxTotalCapturedBytes = 512 * 1024;

    public async Task<Result<CreateCodeReviewAttemptCommandResult>> HandleAsync(
        CreateCodeReviewAttemptCommand command, CancellationToken cancellationToken)
    {
        var run = await dbContext.Runs.SingleOrDefaultAsync(candidate => candidate.Id == command.RunId, cancellationToken);
        if (run is null)
        {
            return Result<CreateCodeReviewAttemptCommandResult>.Failure(
                Error.NotFound("runs.not_found", "The requested run was not found."));
        }

        if (run.Lifecycle != RunLifecycle.Running)
        {
            return Result<CreateCodeReviewAttemptCommandResult>.Failure(
                Error.Conflict("runs.not_running", $"The run is {run.Lifecycle} and cannot start a code-review attempt."));
        }

        var alreadyRunning = await dbContext.Attempts.AnyAsync(
            candidate => candidate.RunId == run.Id && candidate.Status == AttemptStatus.Running,
            cancellationToken);
        if (alreadyRunning)
        {
            return Result<CreateCodeReviewAttemptCommandResult>.Failure(
                Error.Conflict("attempts.run_has_active_attempt", "This run already has an attempt in progress."));
        }

        // The run-wide Agent claim budget: every claimed Agent attempt, regardless of role,
        // provider, dispatch, result, or interruption, permanently consumes one slot. Checked
        // before any provider-availability probe or evidence capture so an exhausted run never
        // invokes a provider. The filtered unique index on (RunId, AgentBudgetSlot) is the
        // database backstop for the race this count-and-compare check alone cannot close.
        var agentAttemptsUsed = await dbContext.Attempts.CountAsync(
            candidate => candidate.RunId == run.Id && candidate.Kind == AttemptKind.Agent,
            cancellationToken);
        if (agentAttemptsUsed >= run.MaximumAgentAttempts)
        {
            return Result<CreateCodeReviewAttemptCommandResult>.Failure(
                Error.Conflict("agent_attempts.budget_exhausted", "This run has reached its maximum claimed Agent attempts."));
        }

        var workspace = await dbContext.GitWorkspaces
            .Where(candidate => candidate.ProjectId == run.ProjectId)
            .OrderByDescending(candidate => candidate.WorkspaceNumber)
            .FirstOrDefaultAsync(cancellationToken);
        if (workspace is null || workspace.Status != WorkspaceStatus.Ready)
        {
            return Result<CreateCodeReviewAttemptCommandResult>.Failure(
                Error.Conflict("agent_attempts.workspace_not_ready", "A ready isolated workspace is required to request a code review."));
        }

        var leaseIsActive = await dbContext.RepositoryMutationLeases
            .AnyAsync(lease => lease.WorkspaceId == workspace.Id && lease.Status == LeaseStatus.Active, cancellationToken);
        if (!leaseIsActive)
        {
            return Result<CreateCodeReviewAttemptCommandResult>.Failure(
                Error.Conflict("agent_attempts.lease_not_active", "An active workspace lease is required to request a code review."));
        }

        var checkpoint = await dbContext.GitCheckpoints
            .Where(candidate => candidate.WorkspaceId == workspace.Id)
            .OrderByDescending(candidate => candidate.CheckpointNumber)
            .FirstOrDefaultAsync(cancellationToken);
        if (checkpoint is null)
        {
            return Result<CreateCodeReviewAttemptCommandResult>.Failure(
                Error.Conflict("agent_attempts.checkpoint_missing", "A current Git checkpoint is required to request a code review."));
        }

        var codexSnapshot = await dbContext.HostCapabilitySnapshots
            .SingleOrDefaultAsync(candidate => candidate.Capability == Capability.CodexCli, cancellationToken);
        if (codexSnapshot is null || codexSnapshot.ReasonCode != CapabilityProbeReason.None || string.IsNullOrWhiteSpace(codexSnapshot.ResolvedExecutablePath))
        {
            return Result<CreateCodeReviewAttemptCommandResult>.Failure(
                Error.Conflict("agent_attempts.provider_not_observed", "The Codex runtime is not currently observed as available."));
        }

        var evidence = await evidenceReader.CaptureAsync(workspace.WorkspacePath, cancellationToken);
        if (evidence.Outcome != GitWorkspaceEvidenceOutcome.Success || evidence.FingerprintSha256 != checkpoint.FingerprintSha256)
        {
            return Result<CreateCodeReviewAttemptCommandResult>.Failure(
                Error.Conflict("agent_attempts.checkpoint_not_current", "The selected result checkpoint is no longer current for this workspace."));
        }

        var executionReportValidation = await ValidateExecutionReportAsync(run.Id, command.ExecutionReportMessageId, workspace.Id, checkpoint, cancellationToken);
        if (executionReportValidation.Error is { } error)
        {
            return Result<CreateCodeReviewAttemptCommandResult>.Failure(error);
        }

        var validatedExecutionReport = executionReportValidation.Value!;
        var executionReportMessage = validatedExecutionReport.ExecutionReport;
        var resolvedPlanMessage = validatedExecutionReport.OriginalProposal;

        var verificationValidation = await ValidateVerificationEvidenceAsync(run.ProjectId, checkpoint, cancellationToken);
        if (verificationValidation.Error is { } verificationError)
        {
            return Result<CreateCodeReviewAttemptCommandResult>.Failure(verificationError);
        }

        var orderedVerificationExecutions = verificationValidation.OrderedExecutions!;

        var alreadyReviewed = await CodeReviewInputIdentity.HasCompetingSuccessfulReviewAsync(
            dbContext,
            run.Id,
            attemptId: Guid.Empty,
            executionReportMessage.Id,
            orderedVerificationExecutions.Select(item => item.Execution.Id).ToArray(),
            cancellationToken);
        if (alreadyReviewed)
        {
            return Result<CreateCodeReviewAttemptCommandResult>.Failure(
                Error.Conflict("agent_attempts.already_code_reviewed", "This exact implementation and verification evidence set already has a successful code review."));
        }

        var attemptId = Guid.NewGuid();
        var manifestArtifactId = Guid.NewGuid();
        var nowUtc = timeProvider.GetUtcNow();

        var orderedVerificationEvidence = orderedVerificationExecutions
            .Select(item => new CodeReviewContextManifestBuilder.VerificationEvidence(
                item.CommandName, item.CommandNumber, item.Execution.Status.ToString(), item.Execution.Outcome?.ToString(), item.Execution.ExitCode))
            .ToArray();
        var manifestJson = validatedExecutionReport.PreviousExecutionReport is null
            ? CodeReviewContextManifestBuilder.Build(
                run.ProjectId,
                workspace.Id,
                checkpoint.Id,
                checkpoint.FingerprintSha256,
                run.Objective,
                resolvedPlanMessage.Id,
                resolvedPlanMessage.Summary,
                resolvedPlanMessage.StructuredContentJson,
                executionReportMessage.Id,
                executionReportMessage.Summary,
                executionReportMessage.StructuredContentJson,
                orderedVerificationEvidence,
                evidence.ChangedPaths,
                evidence.CompleteDiff)
            : CodeReviewContextManifestBuilder.BuildForCorrection(
                run.ProjectId,
                workspace.Id,
                checkpoint.Id,
                checkpoint.FingerprintSha256,
                run.Objective,
                resolvedPlanMessage.Id,
                resolvedPlanMessage.Summary,
                resolvedPlanMessage.StructuredContentJson,
                executionReportMessage.Id,
                executionReportMessage.Summary,
                executionReportMessage.StructuredContentJson,
                orderedVerificationEvidence,
                evidence.ChangedPaths,
                evidence.CompleteDiff,
                new CodeReviewContextManifestBuilder.CorrectionEvidence(
                    validatedExecutionReport.PreviousExecutionReport.Id,
                    validatedExecutionReport.PreviousExecutionReport.Summary,
                    validatedExecutionReport.PreviousExecutionReport.StructuredContentJson,
                    validatedExecutionReport.OrderedFindings
                        .Select(finding => new CodeReviewContextManifestBuilder.CorrectionFinding(
                            finding.Id, finding.Summary, finding.StructuredContentJson))
                        .ToArray(),
                    validatedExecutionReport.OrderedRevisionResponses
                        .Select(response => new CodeReviewContextManifestBuilder.CorrectionRevisionResponse(
                            response.Id,
                            response.InReplyToMessageId!.Value,
                            response.Summary,
                            response.StructuredContentJson))
                        .ToArray()));
        if (System.Text.Encoding.UTF8.GetByteCount(manifestJson) > MaxContextManifestBytes)
        {
            return Result<CreateCodeReviewAttemptCommandResult>.Failure(
                Error.Failure("agent_attempts.context_manifest_too_large", "The context manifest exceeds its bound."));
        }

        var manifestPartialPath = artifactStore.GetPartialPath(run.Id, attemptId, ArtifactPurpose.AgentContextManifest);
        Directory.CreateDirectory(Path.GetDirectoryName(manifestPartialPath)!);
        await File.WriteAllTextAsync(manifestPartialPath, manifestJson, cancellationToken);
        var sealedManifest = await artifactStore.SealAsync(run.Id, attemptId, ArtifactPurpose.AgentContextManifest, cancellationToken);
        if (sealedManifest is null)
        {
            return Result<CreateCodeReviewAttemptCommandResult>.Failure(
                Error.Failure("agent_attempts.context_manifest_seal_failed", "The context manifest could not be sealed."));
        }

        var attemptNumber = await dbContext.Attempts.Where(candidate => candidate.RunId == run.Id).CountAsync(cancellationToken) + 1;
        var agentBudgetSlot = agentAttemptsUsed + 1;

        var attempt = Attempt.ClaimAgentCodeReview(
            attemptId,
            run.Id,
            attemptNumber,
            workspace.Id,
            checkpoint.Id,
            checkpoint.FingerprintSha256,
            manifestArtifactId,
            InvocationTimeout,
            MaxBytesPerStream,
            MaxTotalCapturedBytes,
            nowUtc,
            agentBudgetSlot);
        dbContext.Attempts.Add(attempt);

        // The one authoritative record of this attempt's exact durable identity: the reviewed
        // ExecutionReport at sequence 0 (an AttemptInputMessage, exactly like every other Agent
        // role's input), and the exact ordered claimed verification-execution set (a dedicated
        // AttemptVerificationEvidence membership row per execution — never a JSON column).
        var inputMessage = AttemptInputMessage.Record(Guid.NewGuid(), attemptId, executionReportMessage.Id, sequence: 0);
        dbContext.AttemptInputMessages.Add(inputMessage);

        var verificationEvidenceRows = new List<AttemptVerificationEvidence>(orderedVerificationExecutions.Count);
        for (var index = 0; index < orderedVerificationExecutions.Count; index++)
        {
            var item = orderedVerificationExecutions[index];
            verificationEvidenceRows.Add(AttemptVerificationEvidence.Record(
                Guid.NewGuid(), attemptId, item.Execution.VerificationCommandId, item.Execution.Id, sequence: index));
        }

        dbContext.AttemptVerificationEvidence.AddRange(verificationEvidenceRows);

        var manifestArtifact = Artifact.Record(
            manifestArtifactId,
            run.Id,
            attemptId,
            ArtifactPurpose.AgentContextManifest,
            "application/json",
            sealedManifest.RelativeStoragePath,
            sealedManifest.ContentHash,
            sealedManifest.ByteLength,
            truncated: false,
            ArtifactCaptureOutcome.Captured,
            ArtifactSensitivity.HostConstructedContent,
            ArtifactRetentionPolicy.RetainUntilRunDeleted,
            nowUtc);
        dbContext.Artifacts.Add(manifestArtifact);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            var thisAttemptPersisted = await dbContext.Attempts
                .AsNoTracking()
                .AnyAsync(candidate => candidate.Id == attemptId, cancellationToken);
            if (thisAttemptPersisted)
            {
                return Result<CreateCodeReviewAttemptCommandResult>.Success(
                    new CreateCodeReviewAttemptCommandResult(attemptId, attemptNumber));
            }

            artifactStore.DeleteOrphanedSealedFile(run.Id, attemptId, ArtifactPurpose.AgentContextManifest);
            dbContext.Attempts.Remove(attempt);
            dbContext.AttemptInputMessages.Remove(inputMessage);
            foreach (var row in verificationEvidenceRows)
            {
                dbContext.AttemptVerificationEvidence.Remove(row);
            }

            dbContext.Artifacts.Remove(manifestArtifact);

            var competingRunningAttemptExists = await dbContext.Attempts
                .AsNoTracking()
                .AnyAsync(candidate => candidate.RunId == run.Id && candidate.Status == AttemptStatus.Running, cancellationToken);
            if (competingRunningAttemptExists)
            {
                return Result<CreateCodeReviewAttemptCommandResult>.Failure(
                    Error.Conflict("attempts.run_has_active_attempt", "This run already has an attempt in progress."));
            }

            // The race the (RunId, AgentBudgetSlot) unique index exists to close: a concurrent
            // request already consumed the exact slot this request also computed. Below the
            // maximum, this is a safe, retryable conflict — only this one slot number was lost to
            // a faster concurrent claim, not the run's whole budget. Only when the run's real
            // Agent-attempt count has already reached its maximum is this truthfully exhaustion.
            var slotAlreadyClaimedByAnotherAttempt = await dbContext.Attempts.AsNoTracking().AnyAsync(
                candidate => candidate.RunId == run.Id && candidate.Kind == AttemptKind.Agent && candidate.AgentBudgetSlot == agentBudgetSlot,
                cancellationToken);
            if (slotAlreadyClaimedByAnotherAttempt)
            {
                var agentAttemptsUsedNow = await dbContext.Attempts.AsNoTracking().CountAsync(
                    candidate => candidate.RunId == run.Id && candidate.Kind == AttemptKind.Agent, cancellationToken);
                return agentAttemptsUsedNow >= run.MaximumAgentAttempts
                    ? Result<CreateCodeReviewAttemptCommandResult>.Failure(
                        Error.Conflict("agent_attempts.budget_exhausted", "This run has reached its maximum claimed Agent attempts."))
                    : Result<CreateCodeReviewAttemptCommandResult>.Failure(
                        Error.Conflict("agent_attempts.budget_slot_conflict", "A concurrent request already claimed this Agent attempt's budget slot; retry the request."));
            }

            return Result<CreateCodeReviewAttemptCommandResult>.Failure(
                Error.Failure("attempts.persistence_failed", "The attempt could not be durably recorded."));
        }

        return Result<CreateCodeReviewAttemptCommandResult>.Success(
            new CreateCodeReviewAttemptCommandResult(attempt.Id, attempt.AttemptNumber));
    }

    /// <summary>
    /// Validates every fact this slice requires about the named ExecutionReport before this attempt
    /// is ever claimed: it belongs to this run, is a provider-observed Claude ExecutionReport, its
    /// owning Implementer attempt actually completed as Implemented, that attempt's real,
    /// verified result checkpoint is exactly the workspace's current checkpoint this review attempt
    /// is about to claim (never merely "a" current checkpoint by coincidence), and it is the one
    /// and only provider-observed ExecutionReport that Implementer attempt ever produced. Both
    /// initial implementation reports and successfully applied correction reports are accepted by
    /// the shared role-first eligibility policy.
    /// </summary>
    private async Task<ExecutionReportValidation> ValidateExecutionReportAsync(
        Guid runId, Guid executionReportMessageId, Guid workspaceId, GitCheckpoint resultCheckpoint, CancellationToken cancellationToken)
    {
        var message = await dbContext.CollaborationMessages
            .SingleOrDefaultAsync(candidate => candidate.Id == executionReportMessageId, cancellationToken);
        if (message is null || message.RunId != runId)
        {
            return ExecutionReportValidation.Failed(
                Error.NotFound("agent_attempts.execution_report_not_found", "The requested execution report was not found for this run."));
        }

        if (message.Type != CollaborationMessageType.ExecutionReport)
        {
            return ExecutionReportValidation.Failed(
                Error.Conflict(
                    "agent_attempts.not_provider_observed_execution_report",
                    "Only a provider-observed Implementer execution report can be requested for code review."));
        }

        var owningAttempt = await AgentAuthoredMessageEligibility.ResolveOwningAttemptAsync(
            dbContext, message, runId, AgentRole.Implementer, cancellationToken);
        if (owningAttempt is not null
            && (owningAttempt.AgentGitWorkspaceId != workspaceId
                || owningAttempt.AgentResultGitCheckpointId != resultCheckpoint.Id))
        {
            return ExecutionReportValidation.Failed(
                Error.Conflict(
                    "agent_attempts.result_checkpoint_mismatch",
                    "The execution report's result checkpoint is not the workspace's exact current checkpoint."));
        }

        var validation = await ImplementerExecutionReportEligibility.ResolveAsync(
            dbContext, message, runId, workspaceId, resultCheckpoint.Id, cancellationToken);
        return validation is null
            ? ExecutionReportValidation.Failed(
                Error.Conflict(
                    "agent_attempts.implementer_attempt_not_valid",
                    "The execution report's Implementer result chain is not valid for review."))
            : ExecutionReportValidation.Succeeded(validation);
    }

    /// <summary>
    /// Validates the closed verification-evidence chain: at least one verification command is
    /// currently enabled, and every currently enabled command has a latest execution bound to the
    /// exact result checkpoint, terminal, and Passed. Never selects one arbitrary execution when
    /// several enabled commands exist — every enabled command is checked, in a fixed,
    /// deterministic (command-number) order.
    /// </summary>
    private async Task<VerificationEvidenceValidation> ValidateVerificationEvidenceAsync(
        Guid projectId, GitCheckpoint checkpoint, CancellationToken cancellationToken)
    {
        var enabledCommands = await dbContext.VerificationCommands
            .Where(command => command.ProjectId == projectId && command.IsEnabled)
            .OrderBy(command => command.CommandNumber)
            .ToListAsync(cancellationToken);
        if (enabledCommands.Count == 0)
        {
            return VerificationEvidenceValidation.Failed(
                Error.Conflict("agent_attempts.no_verification_commands_enabled", "At least one enabled verification command is required to request a code review."));
        }

        var commandIds = enabledCommands.Select(command => command.Id).ToArray();
        var boundExecutions = await dbContext.VerificationExecutions
            .Where(execution =>
                commandIds.Contains(execution.VerificationCommandId)
                && execution.GitCheckpointId == checkpoint.Id
                && execution.CheckpointFingerprintSha256 == checkpoint.FingerprintSha256)
            .ToListAsync(cancellationToken);

        var latestExecutionByCommand = boundExecutions
            .GroupBy(execution => execution.VerificationCommandId)
            .ToDictionary(group => group.Key, group => group.OrderByDescending(execution => execution.ExecutionNumber).First());

        var orderedExecutions = new List<(VerificationExecution Execution, string CommandName, int CommandNumber)>(enabledCommands.Count);
        foreach (var command in enabledCommands)
        {
            if (!latestExecutionByCommand.TryGetValue(command.Id, out var latestExecution))
            {
                return VerificationEvidenceValidation.Failed(
                    Error.Conflict(
                        "agent_attempts.verification_evidence_missing",
                        $"Enabled verification command '{command.Name}' has no execution bound to the current checkpoint."));
            }

            if (latestExecution.Status == VerificationExecutionStatus.Running)
            {
                return VerificationEvidenceValidation.Failed(
                    Error.Conflict(
                        "agent_attempts.verification_evidence_running",
                        $"Enabled verification command '{command.Name}' has no terminal execution bound to the current checkpoint yet."));
            }

            if (latestExecution.Status != VerificationExecutionStatus.Passed)
            {
                return VerificationEvidenceValidation.Failed(
                    Error.Conflict(
                        "agent_attempts.verification_evidence_not_passed",
                        $"Enabled verification command '{command.Name}' has not Passed for the current checkpoint."));
            }

            orderedExecutions.Add((latestExecution, command.Name, command.CommandNumber));
        }

        return VerificationEvidenceValidation.Succeeded(orderedExecutions);
    }

    private sealed record ExecutionReportValidation(
        ImplementerExecutionReportEligibility.Result? Value,
        Error? Error)
    {
        public static ExecutionReportValidation Succeeded(ImplementerExecutionReportEligibility.Result value) =>
            new(value, null);

        public static ExecutionReportValidation Failed(Error error) => new(null, error);
    }

    private sealed record VerificationEvidenceValidation(
        IReadOnlyList<(VerificationExecution Execution, string CommandName, int CommandNumber)>? OrderedExecutions, Error? Error)
    {
        public static VerificationEvidenceValidation Succeeded(
            IReadOnlyList<(VerificationExecution Execution, string CommandName, int CommandNumber)> orderedExecutions) =>
            new(orderedExecutions, null);

        public static VerificationEvidenceValidation Failed(Error error) => new(null, error);
    }
}
