namespace DevalCopilot.Domain.Features.Runs;

/// <summary>
/// One judgment of an Agent supervision observation at a point in time. When <see cref="ChronologyValid"/> is false nothing else is
/// trustworthy: the observation is unavailable, never fresh. Otherwise <see cref="EffectiveReason"/> is the recorded first attention when
/// one exists and otherwise the attention the policy derives now (null when there is none). A derived attention is effective before
/// it is persisted; <see cref="Recorded"/> says whether the same attention is already durable.
/// </summary>
public readonly record struct AgentSupervisionAssessment(
    bool ChronologyValid, string? EffectiveReason, DateTimeOffset? EffectiveAtUtc, bool Recorded)
{
    public static AgentSupervisionAssessment Unavailable => new(false, null, null, false);
}
