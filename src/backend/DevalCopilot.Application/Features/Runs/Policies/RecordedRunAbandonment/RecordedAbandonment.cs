namespace DevalCopilot.Application.Features.Runs.Policies.RecordedRunAbandonment;

/// <summary>
/// The proved recorded Human abandonment of one run as read now (ADR-0071): the owning project, the exact canonical reason, the
/// recorded time and the one supporting <c>run.abandoned</c> event with its identity and positive sequence. It is unsigned relational
/// consistency, not a present eligibility, an ownership release, a cancellation, a workspace repair or a publication.
/// </summary>
public sealed record RecordedAbandonment(
    Guid ProjectId, string Reason, DateTimeOffset AbandonedAtUtc, Guid EventId, long EventSequence);
