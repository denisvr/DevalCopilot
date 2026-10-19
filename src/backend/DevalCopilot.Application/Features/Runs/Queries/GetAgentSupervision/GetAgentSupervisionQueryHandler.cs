using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Runs.Errors;
using DevalCopilot.Application.Features.Runs.Policies.AgentSupervisions;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Queries.GetAgentSupervision;

public sealed class GetAgentSupervisionQueryHandler(
    IDevalCopilotDbContext dbContext, TimeProvider timeProvider, AgentSupervisionHostInstance host)
    : IQueryHandler<GetAgentSupervisionQuery, Result<GetAgentSupervisionQueryResult>>
{
    public async Task<Result<GetAgentSupervisionQueryResult>> HandleAsync(
        GetAgentSupervisionQuery query, CancellationToken cancellationToken)
    {
        if (!await dbContext.Runs.AsNoTracking().AnyAsync(candidate => candidate.Id == query.RunId, cancellationToken))
        {
            return Result<GetAgentSupervisionQueryResult>.Failure(AgentSupervisionErrors.NotFound());
        }

        var nowUtc = timeProvider.GetUtcNow();

        // Only the columns this disclosure needs: no prompt, path, argument, transcript or output column is ever materialized.
        var attempts = await dbContext.Attempts.AsNoTracking()
            .Where(candidate => candidate.RunId == query.RunId && candidate.Kind == AttemptKind.Agent)
            .Select(candidate => new AttemptRow(
                candidate.Id,
                candidate.AttemptNumber,
                candidate.Status,
                candidate.AgentOutcome,
                candidate.AgentProvider,
                candidate.AgentRole,
                candidate.AgentResponseContract,
                candidate.AgentTimeout,
                candidate.ClaimedAtUtc,
                candidate.AgentDispatchedAtUtc,
                candidate.CompletedAtUtc))
            .ToListAsync(cancellationToken);

        if (attempts.Count == 0)
        {
            return Success(query.RunId, AgentSupervisionStatus.NoAgentAttempt, null, AgentSupervisionVocabulary.NoAgentAttemptRecorded,
                null, null, null, nowUtc);
        }

        var running = attempts.Where(candidate => candidate.Status == AttemptStatus.Running).ToList();
        if (running.Count > 1)
        {
            return Unavailable(query.RunId, AgentSupervisionVocabulary.MultipleActiveAgentAttempts, null, null, nowUtc);
        }

        var selected = running.Count == 1 ? running[0] : attempts.OrderByDescending(candidate => candidate.AttemptNumber).First();
        if (!AgentSupervisionAttempts.IsCoherentAgent(selected.Provider, selected.Role, selected.Contract, selected.Timeout))
        {
            return Unavailable(query.RunId, AgentSupervisionVocabulary.AttemptFactsIncoherent, null, null, nowUtc);
        }

        var attemptView = ToView(selected);
        var terminal = selected.Status != AttemptStatus.Running;
        var receipt = await dbContext.AgentSupervisions.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.AttemptId == selected.Id, cancellationToken);

        // The host clock of the judgment is read after the receipt, never before it: a heartbeat committed while this read ran is then
        // never newer than the clock that judges it, which would otherwise read as an incoherent chronology instead of a fresh report.
        nowUtc = timeProvider.GetUtcNow();
        if (receipt is null)
        {
            return Success(
                query.RunId,
                AgentSupervisionStatus.NotObserved,
                null,
                terminal ? AgentSupervisionVocabulary.ReviewTheRecordedResult : AgentSupervisionVocabulary.InspectTheRecordedAttempt,
                attemptView,
                null,
                null,
                nowUtc);
        }

        if (receipt.RunId != query.RunId || receipt.AttemptId != selected.Id)
        {
            return Unavailable(query.RunId, AgentSupervisionVocabulary.SupervisionFactsIncoherent, attemptView, null, nowUtc);
        }

        var observation = ToView(receipt, receipt.HostInstanceId == host.Id);
        var ownedByCurrentHost = receipt.HostInstanceId == host.Id;
        if (terminal)
        {
            // The Attempt's own terminal facts govern: the observation is judged at the recorded completion, so a timely result read
            // late is no attention and a late one stays visible as history. The current chronology is checked first: a closure, first
            // attention or completion recorded after the current host clock is impossible and unavailable, never clamped to now.
            var judged = AgentSupervisionAttempts.AssessTerminal(receipt, selected.CompletedAtUtc, nowUtc);
            if (!judged.ChronologyValid)
            {
                return Unavailable(
                    query.RunId, AgentSupervisionVocabulary.ChronologyInvalid, attemptView, observation, nowUtc);
            }

            return Success(
                query.RunId,
                AgentSupervisionStatus.Ended,
                null,
                AgentSupervisionVocabulary.ReviewTheRecordedResult,
                attemptView,
                observation,
                ToView(judged, receipt),
                nowUtc);
        }

        var assessment = AgentSupervisionPolicy.Assess(receipt, nowUtc, ownedByCurrentHost);
        if (!assessment.ChronologyValid)
        {
            return Unavailable(query.RunId, AgentSupervisionVocabulary.ChronologyInvalid, attemptView, observation, nowUtc);
        }

        if (assessment.EffectiveReason is not null)
        {
            return Success(
                query.RunId,
                AgentSupervisionStatus.NeedsAttention,
                assessment.EffectiveReason,
                AgentSupervisionVocabulary.InspectAndReconcileBeforeFurtherWork,
                attemptView,
                observation,
                ToView(assessment, receipt),
                nowUtc);
        }

        // A closed observation of a Running attempt always carries an attention; a closure without one is not a coherent record.
        if (!receipt.IsOpen)
        {
            return Unavailable(query.RunId, AgentSupervisionVocabulary.SupervisionFactsIncoherent, attemptView, observation, nowUtc);
        }

        return Success(
            query.RunId, AgentSupervisionStatus.Observed, null, AgentSupervisionVocabulary.WaitAndRefresh, attemptView, observation, null, nowUtc);
    }

    private static AgentSupervisionAttemptView ToView(AttemptRow row) => new(
        row.Id,
        row.AttemptNumber,
        row.Role!.Value,
        row.Provider!.Value,
        row.Contract!.Value,
        row.Status,
        row.Outcome,
        row.ClaimedAtUtc,
        row.DispatchedAtUtc,
        row.CompletedAtUtc);

    private static AgentSupervisionObservationView ToView(AgentSupervision receipt, bool ownedByCurrentHost) => new(
        receipt.HostInstanceId,
        receipt.ObserverId,
        ownedByCurrentHost,
        receipt.Revision,
        receipt.Phase,
        receipt.StartedAtUtc,
        receipt.LastHeartbeatAtUtc,
        receipt.PhaseEnteredAtUtc,
        receipt.PhaseDeadlineUtc,
        receipt.ClosureReason,
        receipt.ClosedAtUtc);

    private static AgentSupervisionAttentionView? ToView(AgentSupervisionAssessment assessment, AgentSupervision receipt) =>
        assessment.EffectiveReason is { } reason && assessment.EffectiveAtUtc is { } at
            ? new AgentSupervisionAttentionView(
                reason, at, assessment.Recorded ? receipt.AttentionReason : null, assessment.Recorded ? receipt.AttentionAtUtc : null)
            : null;

    private static Result<GetAgentSupervisionQueryResult> Unavailable(
        Guid runId,
        string reasonCode,
        AgentSupervisionAttemptView? attempt,
        AgentSupervisionObservationView? observation,
        DateTimeOffset nowUtc) => Success(
        runId,
        AgentSupervisionStatus.Unavailable,
        reasonCode,
        AgentSupervisionVocabulary.InspectAndReconcileBeforeFurtherWork,
        attempt,
        observation,
        null,
        nowUtc);

    private static Result<GetAgentSupervisionQueryResult> Success(
        Guid runId,
        AgentSupervisionStatus status,
        string? reasonCode,
        string nextActionCode,
        AgentSupervisionAttemptView? attempt,
        AgentSupervisionObservationView? observation,
        AgentSupervisionAttentionView? attention,
        DateTimeOffset nowUtc) => Result<GetAgentSupervisionQueryResult>.Success(
        new GetAgentSupervisionQueryResult(runId, status, reasonCode, nextActionCode, attempt, observation, attention, nowUtc));

    private sealed record AttemptRow(
        Guid Id,
        int AttemptNumber,
        AttemptStatus Status,
        AgentOutcome? Outcome,
        AgentProvider? Provider,
        AgentRole? Role,
        AgentResponseContract? Contract,
        TimeSpan? Timeout,
        DateTimeOffset ClaimedAtUtc,
        DateTimeOffset? DispatchedAtUtc,
        DateTimeOffset? CompletedAtUtc);
}
