namespace DevalCopilot.Application.Features.Runs.Queries.GetManualRunAbandonment;

/// <summary>The recorded reason and time of a coherently abandoned run.</summary>
public sealed record ManualRunAbandonmentView(string Reason, DateTimeOffset AbandonedAtUtc);
