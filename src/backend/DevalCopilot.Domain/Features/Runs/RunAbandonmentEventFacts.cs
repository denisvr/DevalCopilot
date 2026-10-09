namespace DevalCopilot.Domain.Features.Runs;

/// <summary>One stored <see cref="RunEventType.RunAbandoned"/> event as the coherence rule sees it.</summary>
/// <param name="IsHumanAndRunScoped">True only for a Human actor without role or provider and without an attempt.</param>
/// <param name="Reason">The reason carried by the event payload, or <see langword="null"/> when it is absent or malformed.</param>
/// <param name="OccurredAtUtc">When the event was recorded.</param>
public sealed record RunAbandonmentEventFacts(bool IsHumanAndRunScoped, string? Reason, DateTimeOffset OccurredAtUtc);
