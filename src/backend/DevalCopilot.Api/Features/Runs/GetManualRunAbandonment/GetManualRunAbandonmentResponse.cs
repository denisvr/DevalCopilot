namespace DevalCopilot.Api.Features.Runs.GetManualRunAbandonment;

/// <summary>Advisory eligibility plus the recorded abandonment. <c>Abandonment</c> is present only for a coherently abandoned run.</summary>
public sealed record GetManualRunAbandonmentResponse(
    bool Eligible,
    string? RefusalCode,
    ManualRunAbandonmentResponse? Abandonment,
    long LatestEventSequence);
