using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Runs.Queries.GetAgentAttemptStatus;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Queries.GetImplementationAttemptStatus;

public sealed class GetImplementationAttemptStatusQueryHandler(IDevalCopilotDbContext dbContext)
    : IQueryHandler<GetImplementationAttemptStatusQuery, Result<ImplementationAttemptStatusQueryResult>>
{
    // Mirrors the adapter contract version already fixed at claim time (Attempt.ClaimAgentImplementation)
    // and the fixed CLI argument hardcoded in ClaudeImplementationAdapter's own "--permission-mode" entry.
    // Duplicated here rather than shared across layers, exactly like AdapterContractVersion is already
    // duplicated between Attempt.cs and CreateImplementationAttemptCommandHandler.
    private const string ClaudeImplementerAdapterContractVersion = "claude-implementation-v1";
    private const string ConfiguredClaudeImplementerPermissionMode = "acceptEdits";

    // Mirrors the current adapter's own hardcoded "--no-session-persistence" argument
    // (ClaudeImplementationAdapter) — a fixed CLI configuration fact, never a provider-observed
    // result and never DevalCopilot's own durable attempt history.
    private const string ConfiguredClaudeImplementerSessionPersistence = "Disabled";

    // Mirrors the current adapter's own hardcoded "--permission-prompts none" argument
    // (ClaudeImplementationAdapter) — a fixed CLI configuration fact, never a provider-observed
    // result and never invocation eligibility.
    private const string ConfiguredClaudeImplementerPermissionPrompts = "None";

    // Mirrors the current adapter's own hardcoded "--no-session-persistence" argument
    // (ClaudeImplementationAdapter): the Claude Code CLI reference documents that a session
    // started under this flag cannot be resumed. A fixed CLI configuration fact about this
    // attempt's adapter contract, never a provider-observed result, never a host-wide capability
    // assessment, and never invocation eligibility.
    private const string ConfiguredClaudeImplementerResumeEligibility = "Ineligible";

    public async Task<Result<ImplementationAttemptStatusQueryResult>> HandleAsync(
        GetImplementationAttemptStatusQuery query, CancellationToken cancellationToken)
    {
        var runExists = await dbContext.Runs.AsNoTracking().AnyAsync(run => run.Id == query.RunId, cancellationToken);
        if (!runExists)
        {
            return Result<ImplementationAttemptStatusQueryResult>.Failure(
                Error.NotFound("runs.not_found", "The requested run was not found."));
        }

        Attempt? attempt;
        try
        {
            attempt = await dbContext.Attempts
                .AsNoTracking()
                .Where(candidate =>
                    candidate.RunId == query.RunId && candidate.Kind == AttemptKind.Agent && candidate.AgentRole == AgentRole.Implementer)
                .OrderByDescending(candidate => candidate.AttemptNumber)
                .FirstOrDefaultAsync(cancellationToken);
        }
        catch (ArgumentException)
        {
            return InvalidAssignment();
        }
        catch (FormatException)
        {
            return InvalidAssignment();
        }
        catch (InvalidOperationException)
        {
            return InvalidAssignment();
        }

        if (attempt is null)
        {
            return Result<ImplementationAttemptStatusQueryResult>.Success(ImplementationAttemptStatusQueryResult.NoAttempt);
        }

        var assignment = attempt.GetAssignmentSnapshot();
        if (assignment is null)
        {
            return InvalidAssignment();
        }

        var planProposalMessageId = await ImplementationInputIdentity.GetPlanProposalMessageIdAsync(dbContext, attempt.Id, cancellationToken);

        var artifacts = await dbContext.Artifacts
            .AsNoTracking()
            .Where(artifact => artifact.AttemptId == attempt.Id)
            .Select(artifact => new AgentAttemptArtifactMetadata(artifact.Purpose, artifact.ByteLength, artifact.Truncated, artifact.CaptureOutcome))
            .ToArrayAsync(cancellationToken);

        string? resultCheckpointFingerprint = null;
        IReadOnlyList<string> changedRelativePaths = [];
        if (attempt.AgentResultGitCheckpointId is { } resultCheckpointId)
        {
            resultCheckpointFingerprint = await dbContext.GitCheckpoints
                .AsNoTracking()
                .Where(checkpoint => checkpoint.Id == resultCheckpointId)
                .Select(checkpoint => checkpoint.FingerprintSha256)
                .SingleOrDefaultAsync(cancellationToken);

            changedRelativePaths = await dbContext.GitChangedFiles
                .AsNoTracking()
                .Where(changedFile => changedFile.CheckpointId == resultCheckpointId)
                .Select(changedFile => changedFile.Path)
                .ToArrayAsync(cancellationToken);
        }

        string? executionReportSummary = null;
        if (attempt.AgentOutcome == AgentOutcome.Implemented)
        {
            executionReportSummary = await dbContext.CollaborationMessages
                .AsNoTracking()
                .Where(message => message.AttemptId == attempt.Id && message.Type == CollaborationMessageType.ExecutionReport)
                .Select(message => message.Summary)
                .SingleOrDefaultAsync(cancellationToken);
        }

        var isCoherentDefaultImplementationAssignment =
            attempt.AgentRole == AgentRole.Implementer
            && assignment.Provider == AgentProvider.ClaudeCode
            && assignment.PermissionProfile == AgentPermissionProfile.WorkspaceEditOnly
            && assignment.AdapterContractVersion == ClaudeImplementerAdapterContractVersion;

        var configuredPermissionMode = isCoherentDefaultImplementationAssignment
            ? ConfiguredClaudeImplementerPermissionMode
            : null;

        var configuredSessionPersistence = isCoherentDefaultImplementationAssignment
            ? ConfiguredClaudeImplementerSessionPersistence
            : null;

        var configuredPermissionPrompts = isCoherentDefaultImplementationAssignment
            ? ConfiguredClaudeImplementerPermissionPrompts
            : null;

        var configuredResumeEligibility = isCoherentDefaultImplementationAssignment
            ? ConfiguredClaudeImplementerResumeEligibility
            : null;

        return Result<ImplementationAttemptStatusQueryResult>.Success(new ImplementationAttemptStatusQueryResult(
            true,
            attempt.Id,
            attempt.AttemptNumber,
            planProposalMessageId,
            attempt.Status,
            attempt.AgentOutcome,
            attempt.AgentGitCheckpointId,
            attempt.AgentCheckpointFingerprintSha256,
            attempt.AgentResultGitCheckpointId,
            resultCheckpointFingerprint,
            executionReportSummary,
            changedRelativePaths,
            attempt.ClaimedAtUtc,
            attempt.AgentDispatchedAtUtc,
            attempt.CompletedAtUtc,
            artifacts,
            assignment,
            attempt.AgentRole,
            attempt.GetAgentProcessExecutionEvidence(),
            attempt.AgentTimeout,
            attempt.GetAgentTokenUsageEvidence(),
            configuredPermissionMode,
            configuredSessionPersistence,
            configuredPermissionPrompts,
            configuredResumeEligibility));
    }

    private static Result<ImplementationAttemptStatusQueryResult> InvalidAssignment() =>
        Result<ImplementationAttemptStatusQueryResult>.Failure(
            Error.Failure("agent_attempts.invalid_assignment", "Implementation assignment metadata is unavailable."));
}
