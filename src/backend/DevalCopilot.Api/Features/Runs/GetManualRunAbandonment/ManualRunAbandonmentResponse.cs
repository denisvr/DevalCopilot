namespace DevalCopilot.Api.Features.Runs.GetManualRunAbandonment;

/// <summary>The recorded reason and UTC time of a coherently abandoned run.</summary>
public sealed record ManualRunAbandonmentResponse(string Reason, DateTimeOffset AbandonedAtUtc);
