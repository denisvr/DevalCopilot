using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Features.Runs.Policies;

namespace DevalCopilot.Application.Features.Runs.Commands.MarkAgentAttemptDispatched;

/// <summary>
/// The execution-start claim: commits, in one short transaction, that this Agent attempt's
/// provider is about to be invoked — before the hosted supervisor ever calls the Codex adapter.
/// If this fails, the supervisor must not invoke the adapter; if it succeeds, the attempt is
/// never eligible for dispatch again. <paramref name="ExpectedDirectGuidance"/> is supplied by the mutation
/// supervisors with the snapshot their feed projected; an attempt that recorded direct guidance is not dispatched
/// without it, and the fresh persisted snapshot must equal it. <paramref name="ExpectedPlanningAuthorization"/> is the
/// implementation supervisor's projection of the human plan authorization the attempt consumed (ADR-0016); the fresh
/// durable facts must equal it, an authorized attempt is never dispatched without it, and an ordinary one never with it.
/// <paramref name="ExpectedAccountUsageGuard"/> is a Codex supervisor's own fresh guard facts for the run-scoped account-usage stop
/// (ADR-0025): bound to this attempt, its threshold snapshot and the launch tuple it is about to use. An attempt that snapshotted a
/// threshold is never dispatched without them, and they are re-validated against the attempt's stored threshold, the current vetted
/// launch tuple and the freshness rules; an attempt without a snapshot is never dispatched with them. Internal to the host: no HTTP
/// contract accepts it.
/// </summary>
public sealed record MarkAgentAttemptDispatchedCommand(
    Guid RunId,
    Guid AttemptId,
    ExpectedDirectHumanGuidance? ExpectedDirectGuidance = null,
    PlanningImplementationAuthorizationFact? ExpectedPlanningAuthorization = null,
    CodexAccountUsageGuardFacts? ExpectedAccountUsageGuard = null) : ICommand<Result<DateTimeOffset>>;
