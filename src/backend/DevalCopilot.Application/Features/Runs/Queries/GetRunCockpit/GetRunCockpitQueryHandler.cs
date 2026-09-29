using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Queries.GetRunCockpit;

public sealed class GetRunCockpitQueryHandler(IDevalCopilotDbContext dbContext, TimeProvider timeProvider)
    : IQueryHandler<GetRunCockpitQuery, Result<GetRunCockpitQueryResult>>
{
    private static readonly RunStage[] StageSequence =
    [
        RunStage.Intake,
        RunStage.Plan,
        RunStage.Critique,
        RunStage.Resolution,
        RunStage.Execute,
        RunStage.Completed,
    ];

    public async Task<Result<GetRunCockpitQueryResult>> HandleAsync(
        GetRunCockpitQuery query,
        CancellationToken cancellationToken)
    {
        var projection = await dbContext.Runs
            .AsNoTracking()
            .Where(run => run.Id == query.RunId)
            .Join(
                dbContext.Projects.AsNoTracking(),
                run => run.ProjectId,
                project => project.Id,
                (run, project) => new { Run = run, ProjectName = project.Name })
            .SingleOrDefaultAsync(cancellationToken);

        if (projection is null)
        {
            return Result<GetRunCockpitQueryResult>.Failure(
                Error.NotFound("runs.not_found", "The requested run was not found."));
        }

        var run = projection.Run;
        var latestSequence = await dbContext.Events
            .AsNoTracking()
            .Where(runEvent => runEvent.RunId == run.Id)
            .Select(runEvent => (long?)runEvent.Sequence)
            .MaxAsync(cancellationToken) ?? 0;

        var autonomousDurationSeconds = run.AccumulatedAutonomousSeconds;
        if (run.Lifecycle == RunLifecycle.Running)
        {
            autonomousDurationSeconds += (timeProvider.GetUtcNow() - run.LastAdvancedAtUtc).TotalSeconds;
        }

        // The latest-attempt card needs the full entity. If that one row carries an unrecognized
        // persisted enum value it cannot be materialized: the card is then omitted rather than failing
        // the whole cockpit, so the stop projection below (which classifies rows without
        // materializing them) is still delivered. Nothing about the failure is exposed.
        Attempt? latestAgentAttempt;
        try
        {
            latestAgentAttempt = await dbContext.Attempts
                .AsNoTracking()
                .Where(attempt => attempt.RunId == run.Id && attempt.Kind == AttemptKind.Agent)
                .OrderByDescending(attempt => attempt.AttemptNumber)
                .FirstOrDefaultAsync(cancellationToken);
        }
        catch (InvalidOperationException)
        {
            latestAgentAttempt = null;
        }

        // The run-wide Agent claim-budget projection: every claimed Agent attempt permanently
        // consumes one slot regardless of role, provider, dispatch, result, or interruption —
        // distinct from, and never combined with, the review-correction-specific budget exposed
        // by GetReviewCorrectionAttemptStatus.
        var agentAttemptsUsed = await dbContext.Attempts
            .AsNoTracking()
            .CountAsync(attempt => attempt.RunId == run.Id && attempt.Kind == AttemptKind.Agent, cancellationToken);

        // Only the persisted usage/process-evidence members of dispatched Agent attempts are
        // read — never a prompt, output, path, or session identifier. Each row is reconstructed
        // through the same Domain rules as Attempt.GetAgentTokenUsageEvidence and
        // Attempt.GetAgentProcessExecutionEvidence, so an inconsistent row counts as unknown
        // usage/evidence rather than being partially summed. The attempt's own Status is passed
        // alongside its usage so a still-Running attempt is always bucketed as pending, never as
        // known usage, even if its persisted row already carries seemingly-valid token fields. One
        // projection and one pass feed both accumulators below, keeping this bounded to a minimal
        // per-attempt shape rather than materializing full Attempt entities or querying twice.
        var dispatchedAttemptEvidence = dbContext.Attempts
            .AsNoTracking()
            .Where(attempt => attempt.RunId == run.Id && attempt.Kind == AttemptKind.Agent && attempt.AgentDispatchedAtUtc != null)
            .Select(attempt => new
            {
                IsRunning = attempt.Status == AttemptStatus.Running,
                IsTerminal = attempt.Status == AttemptStatus.Completed
                    || attempt.Status == AttemptStatus.Failed
                    || attempt.Status == AttemptStatus.Interrupted,
                IsCodexProvider = attempt.AgentProvider == AgentProvider.Codex,
                IsClaudeProvider = attempt.AgentProvider == AgentProvider.ClaudeCode,
                IsCodexPair = attempt.AgentProvider == AgentProvider.Codex
                    && (attempt.AgentRole == AgentRole.Planner
                        || attempt.AgentRole == AgentRole.Resolver
                        || attempt.AgentRole == AgentRole.CodeReviewer),
                IsClaudePair = attempt.AgentProvider == AgentProvider.ClaudeCode
                    && (attempt.AgentRole == AgentRole.CriticalReviewer || attempt.AgentRole == AgentRole.Implementer),
                attempt.AgentInputTokens,
                attempt.AgentOutputTokens,
                attempt.AgentCacheCreationInputTokens,
                attempt.AgentCacheReadInputTokens,
                attempt.AgentTokenUsageSchemaVersion,
                attempt.AgentProcessOutcome,
                attempt.AgentProcessExitCode,
                attempt.AgentProcessDuration,
            })
            .AsAsyncEnumerable();

        var tokenUsageAccumulator = new RunCockpitTokenUsageAccumulator();
        var providerTokenUsageAccumulator = new RunCockpitProviderTokenUsageAccumulator();
        var tokenWarningAccumulator = new RunCockpitTokenWarningAccumulator();
        var tokenStopAccumulator = new AgentTokenStopAccumulator();
        var processDurationAccumulator = new RunCockpitAgentProcessDurationAccumulator();
        await foreach (var attempt in dispatchedAttemptEvidence.WithCancellation(cancellationToken))
        {
            // Status and provider were classified by the database, never materialized through the
            // string-enum conversion. For the advisory and raw projections an unrecognized status is
            // pending (never counted) and an unrecognized provider is unattributed; the stop
            // additionally requires a coherent role/provider pair (see PersistedAgentAttemptStopEvidence).
            var status = attempt.IsTerminal ? AttemptStatus.Completed : AttemptStatus.Running;
            AgentProvider? provider = attempt.IsCodexProvider
                ? AgentProvider.Codex
                : attempt.IsClaudeProvider ? AgentProvider.ClaudeCode : null;
            var tokenUsageEvidence = AgentTokenUsageEvidence.FromPersisted(
                provider,
                attempt.AgentInputTokens,
                attempt.AgentOutputTokens,
                attempt.AgentCacheCreationInputTokens,
                attempt.AgentCacheReadInputTokens,
                attempt.AgentTokenUsageSchemaVersion);

            tokenUsageAccumulator.Add(status, tokenUsageEvidence);
            providerTokenUsageAccumulator.Add(status, provider, tokenUsageEvidence);
            tokenWarningAccumulator.Add(status, provider, tokenUsageEvidence);

            var stopEvidence = new PersistedAgentAttemptStopEvidence(
                attempt.IsRunning,
                attempt.IsTerminal,
                attempt.IsCodexPair,
                attempt.IsClaudePair,
                attempt.AgentInputTokens,
                attempt.AgentOutputTokens,
                attempt.AgentCacheCreationInputTokens,
                attempt.AgentCacheReadInputTokens,
                attempt.AgentTokenUsageSchemaVersion);
            var (stopStatus, stopProvider) = stopEvidence.Classify();
            tokenStopAccumulator.Add(stopStatus, stopProvider, stopEvidence.Usage(stopProvider));

            AgentProcessExecutionEvidence? processEvidence = null;
            if (attempt.AgentProcessOutcome is { } processOutcome
                && attempt.AgentProcessDuration is { } processDuration
                && AgentProcessExecutionEvidence.Validate(processOutcome, attempt.AgentProcessExitCode, processDuration) is null)
            {
                processEvidence = AgentProcessExecutionEvidence.Create(processOutcome, attempt.AgentProcessExitCode, processDuration);
            }

            processDurationAccumulator.Add(status, processEvidence);
        }

        var tokenUsageSummary = tokenUsageAccumulator.ToSummary();
        var providerTokenUsageSummaries = providerTokenUsageAccumulator.ToEntries();
        var agentProcessDurationSummary = processDurationAccumulator.ToSummary();

        // The independent run-wide Agent invocation-TIME budget projection: never combined with the
        // count-budget fields above, and never a fabricated policy for a historical Run that
        // predates this decision.
        RunCockpitAgentInvocationTimeBudgetSummary agentInvocationTimeBudget;
        if (run.MaximumAgentInvocationTime is not { } maximumAgentInvocationTime)
        {
            agentInvocationTimeBudget = RunCockpitAgentInvocationTimeBudgetSummary.LegacyUnknown();
        }
        else
        {
            var reservedAgentInvocationTime = await AgentInvocationTimeBudget.ComputeReservedAsync(dbContext, run.Id, asNoTracking: true, cancellationToken);
            agentInvocationTimeBudget = reservedAgentInvocationTime is null
                ? RunCockpitAgentInvocationTimeBudgetSummary.EvidenceInvalidFor(maximumAgentInvocationTime)
                : RunCockpitAgentInvocationTimeBudgetSummary.Budgeted(maximumAgentInvocationTime, reservedAgentInvocationTime.Value);
        }

        var agentClaimPathTimeFits = RunCockpitAgentClaimPathTimeFitProjection.Compute(agentInvocationTimeBudget);

        var stageMap = StageSequence
            .Select(stage => new RunCockpitStageEntry(stage, IsCompleted: stage < run.Stage, IsActive: stage == run.Stage))
            .ToArray();

        return Result<GetRunCockpitQueryResult>.Success(
            new GetRunCockpitQueryResult(
                run.Id,
                run.ProjectId,
                projection.ProjectName,
                run.ExecutionNumber,
                run.Objective,
                run.Lifecycle,
                run.Stage,
                run.ActiveParticipant,
                autonomousDurationSeconds,
                latestSequence,
                stageMap,
                CanPause: false,
                CanStop: false,
                tokenUsageSummary,
                providerTokenUsageSummaries,
                run.MaximumAgentAttempts,
                agentAttemptsUsed,
                agentAttemptsUsed >= run.MaximumAgentAttempts,
                agentInvocationTimeBudget,
                agentProcessDurationSummary,
                agentClaimPathTimeFits,
                run.RequestedCodexModel,
                run.RequestedCodexEffort,
                latestAgentAttempt is null
                    ? null
                    : new RunCockpitAgentAttemptEntry(
                        latestAgentAttempt.Id,
                        latestAgentAttempt.AttemptNumber,
                        latestAgentAttempt.AgentRole,
                        latestAgentAttempt.AgentProvider,
                        latestAgentAttempt.Status,
                        latestAgentAttempt.AgentOutcome,
                        latestAgentAttempt.AgentDispatchedAtUtc,
                        latestAgentAttempt.GetAgentProcessExecutionEvidence(),
                        latestAgentAttempt.AgentTimeout,
                        latestAgentAttempt.GetAgentTokenUsageEvidence(),
                        latestAgentAttempt.AgentRequestedModel,
                        latestAgentAttempt.AgentRequestedEffort),
                run.RequestedClaudeModel,
                run.RequestedClaudeEffort,
                tokenWarningAccumulator.ToEntries(run.CodexTokenWarningThreshold, run.ClaudeTokenWarningThreshold),
                [
                    tokenStopAccumulator.ToEvaluation(AgentProvider.Codex, run.CodexTokenStopThreshold),
                    tokenStopAccumulator.ToEvaluation(AgentProvider.ClaudeCode, run.ClaudeTokenStopThreshold),
                ]));
    }
}
